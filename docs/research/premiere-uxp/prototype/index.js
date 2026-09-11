/* Disposable API experiment. No production imports, Catalog access, or arbitrary commands. */
const ppro = require('premierepro');
const uxp = require('uxp');
const fs = uxp.storage.localFileSystem;
const projectName = 'Lightflow-256-disposable.prproj';
let busy = false;
const normalize = value => value.replace(/\\/g, '/').toLowerCase();
const guid = value => value ? value.toString() : null;
const id = item => typeof item.getId === 'function' ? item.getId() : ppro.ProjectItem.cast(item).getId();
const tick = value => ppro.TickTime.createWithTicks(value);

function transaction(project, label, createActions) {
  let result;
  project.lockedAccess(() => {
    // Since 26.3, Action creation as well as execution must stay inside the lock.
    result = project.executeTransaction(compound => {
      for (const action of createActions()) compound.addAction(action);
    }, label);
  });
  if (!result) throw new Error(`Transaction failed: ${label}`);
}

async function walk(folder) {
  const result = [];
  for (const item of await folder.getItems()) {
    result.push(item);
    let child;
    try { child = ppro.FolderItem.cast(item); } catch (_) { /* not a bin */ }
    if (child) result.push(...await walk(child));
  }
  return result;
}

async function main() {
  if (busy) return;
  busy = true;
  const output = document.getElementById('output');
  const evidence = { startedUtc: new Date().toISOString(), prototype: '0.0.1', steps: [] };
  let folder;
  async function log(step, value) {
    evidence.steps.push({ step, value });
    output.textContent = JSON.stringify(evidence, null, 2);
    console.log(JSON.stringify({ step, value }));
  }
  async function optional(step, action) {
    try { await log(step, await action()); }
    catch (error) { await log(step, { error: String(error) }); }
  }
  try {
    folder = await fs.getFolder();
    if (!folder) return;
    const fixture = JSON.parse(await (await folder.getEntry('fixture.json')).read());
    if (fixture.purpose !== 'Lightflow issue 256 disposable generated media' ||
        fixture.schemaVersion !== 1) throw new Error('Not a generated research fixture folder');
    const source = await folder.getEntry('source.mov');
    const proxy = await folder.getEntry('proxy.mov');
    const projectPath = folder.nativePath + '/' + projectName;
    let project = await ppro.Project.getActiveProject();
    // Never operate on, save, close, or repurpose an existing real project.
    if (project && normalize(project.path) !== normalize(projectPath))
      throw new Error('Close real/other projects before running the disposable proof');
    if (!project) {
      let exists = false;
      try { await folder.getEntry(projectName); exists = true; } catch (_) { /* new fixture */ }
      project = exists ? await ppro.Project.open(projectPath) : await ppro.Project.createProject(projectPath);
    }
    if (!project || normalize(project.path) !== normalize(projectPath))
      throw new Error('Disposable destination verification failed');
    const expectedGuid = guid(project.guid);
    async function guard() {
      const active = await ppro.Project.getActiveProject();
      if (!active || guid(active.guid) !== expectedGuid || normalize(active.path) !== normalize(projectPath))
        throw new Error('Destination changed; stop and reconcile');
    }
    await log('runtime', { host: uxp.host.name, hostVersion: uxp.host.version,
      uxpVersion: uxp.versions && uxp.versions.uxp, applicationPath: uxp.host.applicationPath });
    await log('timebase', { oneSecondTicks: ppro.TickTime.createWithSeconds(1).ticks,
      requestedInTicks: '254016000000', requestedOutTicks: '762048000000' });
    if (ppro.TickTime.createWithSeconds(1).ticks !== '254016000000')
      throw new Error('Unexpected Premiere timebase; do not run fixture mutations');
    await log('activeProject', { guid: expectedGuid, path: project.path, name: project.name,
      resolvedByGuid: guid(ppro.Project.getProject(project.guid).guid) });
    const root = await project.getRootItem();
    let bins = (await root.getItems()).filter(item => item.name === 'Lightflow-256');
    if (bins.length > 1) throw new Error('Ambiguous fixture bin');
    if (!bins.length) {
      await guard();
      transaction(project, 'LF256 create bin', () => [root.createBinAction('Lightflow-256', false)]);
      bins = (await root.getItems()).filter(item => item.name === 'Lightflow-256');
    }
    if (bins.length !== 1) throw new Error('Bin readback failed');
    const bin = ppro.FolderItem.cast(bins[0]);
    await log('bin', { id: id(bin), name: bin.name });
    // This experiment persists destination IDs in the selected disposable folder only.
    let mapping = {};
    try { mapping = JSON.parse(await (await folder.getEntry('mapping.json')).read()); }
    catch (error) {
      const entries = await folder.getEntries();
      if (entries.some(entry => entry.name === 'mapping.json')) throw error;
    }
    if (mapping.projectGuid && mapping.projectGuid !== expectedGuid)
      throw new Error('Fixture mapping belongs to a different project');
    mapping.projectGuid = expectedGuid;
    async function checkpoint() {
      await (await folder.createFile('mapping.json', { overwrite: true })).write(JSON.stringify(mapping, null, 2));
    }
    let items = await walk(root);
    let sourceItem = mapping.sourceId && items.find(item => id(item) === mapping.sourceId);
    if (mapping.sourceId && !sourceItem) throw new Error('Mapped source missing; no blind reimport');
    if (!sourceItem) {
      // Bootstrap only in a controlled empty fixture. Paths are not identity.
      if (items.some(item => item.name === 'source.mov'))
        throw new Error('Unmapped existing source; reconcile uncertain prior import');
      await guard();
      const before = new Set(items.map(id));
      await log('importReturn', await project.importFiles([source.nativePath], true, bin, false));
      items = await walk(root);
      const added = items.filter(item => !before.has(id(item)));
      if (added.length !== 1) throw new Error('Import result ambiguous');
      sourceItem = added[0];
      mapping.sourceId = id(sourceItem);
      mapping.assetId = fixture.assetId;
      await checkpoint();
    }
    const clip = ppro.ClipProjectItem.cast(sourceItem);
    if (normalize(await clip.getMediaFilePath()) !== normalize(source.nativePath))
      throw new Error('Source path verification mismatch');
    await log('source', { assetId: fixture.assetId, id: id(sourceItem), path: await clip.getMediaFilePath() });
    await optional('pathSearchCandidates', async () =>
      (await clip.findItemsMatchingMediaPath(source.nativePath, true)).map(item => ({ id: id(item), name: item.name })));
    await optional('subclip', async () => {
      const all = await walk(root);
      let subclip = mapping.subclipId && all.find(item => id(item) === mapping.subclipId);
      if (mapping.subclipId && !subclip) throw new Error('Mapped subclip missing');
      if (!subclip) {
        if (all.some(item => item.name === 'LF256 native 1-3 seconds')) throw new Error('Unmapped subclip; reconcile');
        const before = new Set(all.map(id));
        await guard();
        transaction(project, 'LF256 native subclip', () => [clip.createSubClipAction(
          'LF256 native 1-3 seconds', tick('254016000000'), tick('762048000000'), true,
          { takeVideo: true, takeAudio: true })]);
        const added = (await walk(root)).filter(item => !before.has(id(item)));
        if (added.length !== 1) throw new Error('Subclip readback ambiguous');
        subclip = added[0];
        mapping.subclipId = id(subclip);
        mapping.lightflowSubclipId = fixture.subclipId;
        await checkpoint();
      }
      const sub = ppro.ClipProjectItem.cast(subclip);
      return { id: id(subclip), name: subclip.name,
        videoIn: (await sub.getInPoint(ppro.Constants.MediaType.VIDEO)).ticks,
        videoOut: (await sub.getOutPoint(ppro.Constants.MediaType.VIDEO)).ticks,
        audioIn: (await sub.getInPoint(ppro.Constants.MediaType.AUDIO)).ticks,
        audioOut: (await sub.getOutPoint(ppro.Constants.MediaType.AUDIO)).ticks,
        hardBoundariesRequested: true, trimmingBeyondBounds: 'requires interactive follow-up' };
    });
    await optional('marker', async () => {
      const markers = await ppro.Markers.getMarkers(clip);
      let marker = mapping.markerGuid && markers.getMarkers().find(m => guid(m.guid) === mapping.markerGuid);
      if (mapping.markerGuid && !marker) throw new Error('Mapped marker missing');
      if (!marker) {
        if (markers.getMarkers().some(m => m.getName() === 'LF256 point')) throw new Error('Unmapped marker; reconcile');
        const before = new Set(markers.getMarkers().map(m => guid(m.guid)));
        await guard();
        transaction(project, 'LF256 add point marker', () => [markers.createAddMarkerAction(
          'LF256 point', 'Comment', tick('508032000000'), tick('0'), 'Disposable Lightflow marker')]);
        const added = markers.getMarkers().filter(m => !before.has(guid(m.guid)));
        if (added.length !== 1) throw new Error('Marker readback ambiguous');
        marker = added[0];
        mapping.markerGuid = guid(marker.guid);
        await checkpoint();
      }
      const pointDuration = marker.getDuration().ticks;
      await guard();
      transaction(project, 'LF256 marker properties', () => [marker.createSetColorByIndexAction(1),
        marker.createSetDurationAction(tick('127008000000'))]);
      return { guid: guid(marker.guid), name: marker.getName(), comments: marker.getComments(),
        type: marker.getType(), colorIndex: marker.getColorIndex(), start: marker.getStart().ticks,
        initialDuration: pointDuration, duration: marker.getDuration().ticks };
    });
    await optional('proxy', async () => {
      const canProxy = await clip.canProxy();
      const before = { hasProxy: await clip.hasProxy(), path: await clip.getProxyPath() };
      if (!canProxy) return { canProxy, before };
      if (before.hasProxy && normalize(before.path) !== normalize(proxy.nativePath))
        throw new Error('Different proxy already attached');
      await guard();
      const attached = before.hasProxy || await clip.attachProxy(proxy.nativePath, false, false);
      return { canProxy, before, attached, after: { hasProxy: await clip.hasProxy(), path: await clip.getProxyPath() },
        undoable: false };
    });
    await guard();
    await log('save', await project.save());
    await optional('loopback', async () => {
      const pairing = JSON.parse(await (await folder.getEntry('pairing.json')).read());
      const response = await fetch('http://127.0.0.1:47856/health', {
        headers: { Authorization: `Bearer ${pairing.token}` }
      });
      if (!response.ok) throw new Error(`Loopback HTTP ${response.status}`);
      return await response.json();
    });
  } catch (error) { await log('stopped', { error: String(error), stack: error.stack }); }
  finally {
    evidence.finishedUtc = new Date().toISOString();
    if (folder) {
      // Only write evidence after the sentinel has been validated.
      try {
        const sentinel = JSON.parse(await (await folder.getEntry('fixture.json')).read());
        if (sentinel.purpose === 'Lightflow issue 256 disposable generated media')
          await (await folder.createFile(`evidence-${Date.now()}.json`)).write(JSON.stringify(evidence, null, 2));
      } catch (error) { console.error(String(error)); }
    }
    output.textContent = JSON.stringify(evidence, null, 2);
    busy = false;
  }
}
document.getElementById('run').addEventListener('click', main);
