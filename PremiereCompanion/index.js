'use strict';
const ppro = require('premierepro');
const uxp = require('uxp');
const { execute, sameProject } = require('./handoff.js');
const { nearestPremiereTicks } = require('./range.js');
const { ProjectBins, enumerateBins } = require('./bins.js');
const { PairingAccess, validatePairing, ENDPOINT } = require('./pairing.js');
const fs = uxp.storage.localFileSystem;
const access = new PairingAccess(fs, localStorage);
const instanceId = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
let pairing = null;
let running = false;
let busy = false;
let lastHealthy = 0;
let timer = null;
let pulseBusy = false;
let pairingInProgress = false;
let connectionGeneration = 0;
const projectBins = new ProjectBins();
const status = text => { document.getElementById('status').textContent = text; };
const id = item => String(typeof item.getId === 'function' ? item.getId() : ppro.ProjectItem.cast(item).getId());
const describe = project => project ? { guid: String(project.guid), path: project.path, name: project.name } : null;

async function walk(bin, depth = 0) {
  if (depth > 50) throw new Error('Project bin nesting exceeds the supported limit.');
  const result = [];
  for (const item of await bin.getItems()) {
    result.push(item);
    let child;
    try { child = ppro.FolderItem.cast(item); } catch (_) { /* clip */ }
    if (child) result.push(...await walk(child, depth + 1));
    if (result.length > 50000) throw new Error('Project exceeds the supported item limit.');
  }
  return result;
}

async function request(path, payload, dispatchId = '') {
  if (!running) throw new Error('Connection paused. Choose Resume Connection.');
  validatePairing(pairing);
  const response = await fetch(ENDPOINT + path, { method: 'POST', redirect: 'error',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${pairing.token}`,
      'X-Lightflow-Session': instanceId, 'X-Lightflow-Dispatch': dispatchId },
    body: JSON.stringify(payload) });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(response.status === 409 ? 'Project, session or version changed. Reconnect to Lightflow.'
    : response.status === 403 ? 'Pairing rejected or expired. Reconnect to Lightflow.' : `Connection problem (${response.status}).`);
  return response.json();
}

async function heartbeat(discoverBins = true) {
  pairing = await access.read();
  if (!running) throw new Error('Connection paused.');
  const project = await ppro.Project.getActiveProject();
  const description = describe(project);
  const bins = await projectBins.read(description, discoverBins, async () =>
    enumerateBins(await project.getRootItem(), item => ppro.FolderItem.cast(item), id));
  const current = describe(await ppro.Project.getActiveProject());
  if ((description || current) && !sameProject(description, current))
    throw new Error('Active project changed while reading bins. Waiting for the current project.');
  await request('/v1/heartbeat', { instanceId, companionVersion: '1.1.0', protocol: 1,
    hostVersion: uxp.host.version, uxpVersion: uxp.versions.uxp, project: description, bins });
  lastHealthy = Date.now();
  return project;
}

const journal = {
  async read(operationId) {
    const data = await fs.getDataFolder();
    const name = `handoff-${operationId}.json`;
    const entries = await data.getEntries();
    const entry = entries.find(item => item.name === name);
    return entry ? JSON.parse(await entry.read()) : null;
  },
  async write(operationId, value) {
    const data = await fs.getDataFolder();
    await (await data.createFile(`handoff-${operationId}.json`, { overwrite: true })).write(JSON.stringify(value));
  }
};

function adapter(project) {
  const premiereTicks = value => ppro.TickTime.createWithTicks(nearestPremiereTicks(value));
  return {
    activeProject: async () => describe(await ppro.Project.getActiveProject()),
    connected: () => running && Date.now() - lastHealthy < 10000,
    async items() {
      const items = [];
      for (const item of await walk(await project.getRootItem())) {
        let mediaPath = null;
        try { mediaPath = await ppro.ClipProjectItem.cast(item).getMediaFilePath(); } catch (_) { /* bin or non-media */ }
        items.push({ id: id(item), name: item.name, mediaPath });
      }
      return items;
    },
    async targetBin(binId, createName, guard) {
      const root = await project.getRootItem();
      const parent = [root, ...await walk(root)].find(item => id(item) === binId);
      if (!parent) throw new Error('Target bin no longer exists.');
      const bin = ppro.FolderItem.cast(parent);
      if (!createName) return bin;
      const existing = (await bin.getItems()).filter(item => item.name === createName);
      if (existing.length > 1) throw new Error('Target bin name is ambiguous.');
      if (existing.length === 1) return ppro.FolderItem.cast(existing[0]);
      await guard();
      let succeeded;
      project.lockedAccess(() => {
        succeeded = project.executeTransaction(compound => compound.addAction(bin.createBinAction(createName, false)), 'Lightflow: create handoff bin');
      });
      if (!succeeded) throw new Error('Could not create the target bin.');
      const matches = (await bin.getItems()).filter(item => item.name === createName);
      if (matches.length !== 1) throw new Error('Created bin readback is ambiguous.');
      return ppro.FolderItem.cast(matches[0]);
    },
    importSource: (path, bin) => project.importFiles([path], true, bin, false),
    async projectRange(itemId, range) {
      if (!range || typeof range !== 'object') throw new Error('Lightflow In/Out range is invalid.');
      const sourceDuration = premiereTicks(range.sourceDurationTicks);
      const inPoint = premiereTicks(range.inTicks);
      const outPoint = premiereTicks(range.outTicks);
      if (BigInt(outPoint.ticks) <= BigInt(inPoint.ticks) || BigInt(outPoint.ticks) > BigInt(sourceDuration.ticks))
        throw new Error('Lightflow In/Out range is invalid.');
      const matches = (await walk(await project.getRootItem())).filter(item => id(item) === itemId);
      if (matches.length !== 1) throw new Error('Imported source item is unavailable for In/Out projection.');
      const clip = ppro.ClipProjectItem.cast(matches[0]);
      const media = await clip.getMedia();
      const actualDuration = await media.getDuration();
      if (BigInt(actualDuration.ticks) < BigInt(outPoint.ticks))
        throw new Error('The imported media duration does not contain Lightflow’s saved Out point.');
      let succeeded = false;
      project.lockedAccess(() => {
        succeeded = project.executeTransaction(compound => compound.addAction(
          clip.createSetInOutPointsAction(inPoint, outPoint)), 'Lightflow: apply source In/Out');
      });
      if (!succeeded) throw new Error('Premiere could not apply the source In/Out points.');
    },
    async clearRange(itemId) {
      const matches = (await walk(await project.getRootItem())).filter(item => id(item) === itemId);
      if (matches.length !== 1) throw new Error('Imported source item is unavailable for In/Out projection.');
      const clip = ppro.ClipProjectItem.cast(matches[0]);
      let succeeded = false;
      project.lockedAccess(() => {
        succeeded = project.executeTransaction(compound => compound.addAction(
          clip.createClearInOutPointsAction()), 'Lightflow: clear source In/Out');
      });
      if (!succeeded) throw new Error('Premiere could not clear the source In/Out points.');
    },
    async createSubclip(sourceItemId, spec) {
      const beforeItems = await walk(await project.getRootItem());
      const matches = beforeItems.filter(item => id(item) === sourceItemId);
      if (matches.length !== 1) throw new Error('Mapped source is unavailable for native Subclip creation.');
      const clip = ppro.ClipProjectItem.cast(matches[0]);
      const media = await clip.getMedia();
      const duration = await media.getDuration();
      const start = spec.range ? premiereTicks(spec.range.inTicks) : ppro.TickTime.createWithTicks('0');
      const end = spec.range ? premiereTicks(spec.range.outTicks) : duration;
      if (BigInt(end.ticks) <= BigInt(start.ticks) || BigInt(end.ticks) > BigInt(duration.ticks))
        throw new Error('The native Subclip range is outside the imported source duration.');
      let succeeded = false;
      project.lockedAccess(() => {
        succeeded = project.executeTransaction(compound => compound.addAction(clip.createSubClipAction(
          spec.name, start, end, spec.hardBoundaries === true,
          { takeVideo: spec.takeVideo === true, takeAudio: spec.takeAudio === true })),
          'Lightflow: create native Subclip');
      });
      if (!succeeded) throw new Error('Premiere could not create the native Subclip.');
      const before = new Set(beforeItems.map(id));
      const added = (await walk(await project.getRootItem())).filter(item => !before.has(id(item)) && item.name === spec.name);
      if (added.length !== 1) throw new Error('Native Subclip readback was ambiguous.');
      return id(added[0]);
    }
  };
}

async function tick() {
  if (!running) return;
  if (busy) {
    if (pulseBusy) return;
    pulseBusy = true;
    try { await heartbeat(false); } catch (_) { lastHealthy = 0; }
    finally { pulseBusy = false; }
    return;
  }
  busy = true;
  try {
    const project = await heartbeat();
    await new Promise(resolve => setTimeout(resolve, 100));
    const command = await request('/v1/poll', {});
    if (command) {
      if (!project || !sameProject(describe(project), command.intent.project)) throw new Error('Accepted project is no longer active.');
      status('Sending source media…');
      const result = await execute(command, adapter(project), journal);
      await new Promise(resolve => setTimeout(resolve, 100));
      await request('/v1/receipt', result, command.dispatchId);
      status(`${result.outcome}: ${result.message}`);
    } else status(`Connected to Lightflow\nPremiere ${uxp.host.version}\nProject: ${project ? project.name : 'No active project'}\nCompanion 1.1.0`);
  } catch (error) {
    lastHealthy = 0;
    status(String(error.message || error));
  } finally { busy = false; }
}

function showConnectionAction() {
  document.getElementById('pair').textContent = access.needsGrant ? 'Allow Connection Access…' : 'Resume Connection';
  document.getElementById('pair').style.display = running ? 'none' : 'inline-block';
  document.getElementById('disconnect').style.display = running ? 'inline-block' : 'none';
}
function stopConnection() {
  connectionGeneration++;
  running = false; pairing = null; lastHealthy = 0;
  clearInterval(timer);
}
async function startConnection(interactive = false) {
  if (pairingInProgress) return;
  pairingInProgress = true;
  const generation = ++connectionGeneration;
  document.getElementById('pair').disabled = true;
  try {
    const restored = await access.restore();
    if (generation !== connectionGeneration) return;
    if (!restored) {
      status('One-time setup: in Lightflow Integration Settings, expand First-time setup and click Copy Setup Location. Choose Allow Connection Access here, paste the location into the folder picker address bar, press Enter, then Select Folder. Adobe remembers this permission.');
      if (!interactive || !await access.grant(() => generation === connectionGeneration)) return;
    }
    if (generation !== connectionGeneration) return;
    if (interactive) access.resume();
    if (access.paused) { status('Connection paused. Choose Resume Connection when ready.'); return; }
    running = true;
    clearInterval(timer);
    timer = setInterval(tick, 1500);
    await tick();
  } catch (error) { running = false; status(String(error.message || error)); }
  finally { pairingInProgress = false; document.getElementById('pair').disabled = false; showConnectionAction(); }
}
document.getElementById('pair').addEventListener('click', () => startConnection(true));
document.getElementById('disconnect').addEventListener('click', () => {
  stopConnection(); access.pause(); showConnectionAction();
  status('Connection paused. Choose Resume Connection when ready.');
});
document.getElementById('forget').addEventListener('click', () => {
  stopConnection(); access.forget(); showConnectionAction();
  status('Remembered connection access removed. Choose Allow Connection Access to set up again.');
});
document.getElementById('troubleshoot').addEventListener('click', () => {
  const panel = document.getElementById('recovery');
  panel.style.display = panel.style.display === 'none' ? 'block' : 'none';
});
startConnection();
