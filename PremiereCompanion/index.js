'use strict';
const ppro = require('premierepro');
const uxp = require('uxp');
const { execute, sameProject } = require('./handoff.js');
const fs = uxp.storage.localFileSystem;
const ENDPOINT = 'http://localhost:47857';
const instanceId = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
let pairing = null;
let folder = null;
let running = false;
let busy = false;
let lastHealthy = 0;
let timer = null;
let pulseBusy = false;
let pairingInProgress = false;
let cachedBins = [];
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
  if (!running || !pairing || pairing.endpoint !== ENDPOINT || Date.parse(pairing.expiresUtc) <= Date.now())
    throw new Error('Pairing expired. Reconnect from Lightflow.');
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
  const project = await ppro.Project.getActiveProject();
  const bins = [];
  if (project && discoverBins) {
    const root = await project.getRootItem();
    bins.push({ id: id(root), name: 'Project root' });
    for (const item of await walk(root)) {
      let bin;
      try { bin = ppro.FolderItem.cast(item); } catch (_) { /* clip */ }
      if (bin) bins.push({ id: id(bin), name: `${bin.name} (${id(bin)})` });
    }
  }
  if (discoverBins) cachedBins = bins;
  await request('/v1/heartbeat', { instanceId, companionVersion: '1.0.2', protocol: 1,
    hostVersion: uxp.host.version, uxpVersion: uxp.versions.uxp, project: describe(project), bins: cachedBins });
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
  return {
    activeProject: async () => describe(await ppro.Project.getActiveProject()),
    connected: () => running && Date.now() - lastHealthy < 10000,
    async items() {
      const items = [];
      for (const item of await walk(await project.getRootItem())) {
        let mediaPath = null;
        try { mediaPath = await ppro.ClipProjectItem.cast(item).getMediaFilePath(); } catch (_) { /* bin or non-media */ }
        items.push({ id: id(item), mediaPath });
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
    importSource: (path, bin) => project.importFiles([path], true, bin, false)
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
    } else status(`Connected to Lightflow\nPremiere ${uxp.host.version}\nProject: ${project ? project.name : 'No active project'}\nCompanion 1.0.2`);
  } catch (error) {
    lastHealthy = 0;
    status(String(error.message || error));
  } finally { busy = false; }
}

document.getElementById('pair').addEventListener('click', async () => {
  if (pairingInProgress) return;
  pairingInProgress = true;
  document.getElementById('pair').disabled = true;
  try {
    folder = await fs.getFolder();
    if (!folder) return;
    pairing = JSON.parse(await (await folder.getEntry('lightflow-pairing.json')).read());
    if (pairing.endpoint !== ENDPOINT || pairing.protocol !== 1 || !/^[A-F0-9]{64}$/.test(pairing.token))
      throw new Error('This is not a supported Lightflow pairing folder.');
    running = true;
    clearInterval(timer);
    timer = setInterval(tick, 1500);
    await tick();
  } catch (error) { running = false; status(String(error.message || error)); }
  finally { pairingInProgress = false; document.getElementById('pair').disabled = false; }
});
document.getElementById('disconnect').addEventListener('click', () => {
  running = false;
  pairing = null;
  clearInterval(timer);
  status('Disconnected. Open Lightflow to pair again.');
});
