'use strict';

// Platform-independent reconciliation policy. The adapter owns every Premiere API call.
const pathKey = value => value.replace(/\\/g, '/').replace(/^\/\/\?\/(?=[a-z]:\/)/i, '').replace(/\/$/, '').toLowerCase();
const sameProject = (left, right) => !!left && !!right && left.guid === right.guid && pathKey(left.path) === pathKey(right.path);

async function execute(command, adapter, journal) {
  const intent = command.intent;
  const receipt = (outcome, itemId, message) => ({ operationId: intent.operationId, outcome, itemId, message });
  let mutationStarted = false;
  const guard = async () => {
    if (!sameProject(await adapter.activeProject(), intent.project))
      throw new Error('Active project changed. Return to the accepted project and reconcile.');
    if (!adapter.connected()) throw new Error('Connection expired. Reconnect before continuing.');
  };
  try {
    await guard();
    const saved = await journal.read(intent.operationId);
    if (saved && saved.intent !== JSON.stringify(intent))
      return receipt('Conflict', null, 'Operation payload changed; no mutation performed.');
    const itemId = saved?.itemId || command.previousReceipt?.itemId;
    let items = await adapter.items();
    if (itemId) {
      const matches = items.filter(item => item.id === itemId);
      if (matches.length !== 1 || pathKey(matches[0].mediaPath || '') !== pathKey(intent.source.path))
        return receipt('Conflict', itemId, 'Mapped source is missing or relinked. Editor undo and edits are preserved; no reimport.');
      await guard();
      return receipt('Verified', itemId, 'Existing Catalog source verified; editor name and bin preserved.');
    }
    // There is no safe exactly-once import across two applications. Never infer identity from a path.
    if (command.previouslyDispatched || saved)
      return receipt('UnknownOutcome', null, 'Prior import outcome is uncertain. Inspect the original project; automatic reimport is blocked.');
    if (items.some(item => item.mediaPath && pathKey(item.mediaPath) === pathKey(intent.source.path)))
      return receipt('Conflict', null, 'Unmapped media already exists in this project. No automatic adoption or duplicate import.');
    const before = new Set(items.map(item => item.id));
    await journal.write(intent.operationId, { intent: JSON.stringify(intent), phase: 'intent' });
    await guard();
    const bin = await adapter.targetBin(intent.binId, intent.createBinName, guard);
    await guard();
    mutationStarted = true;
    await adapter.importSource(intent.source.path, bin);
    items = await adapter.items();
    const added = items.filter(item => !before.has(item.id) && item.mediaPath
      && pathKey(item.mediaPath) === pathKey(intent.source.path));
    if (added.length !== 1) return receipt('UnknownOutcome', null, 'Import readback was ambiguous; reconcile before any retry.');
    await journal.write(intent.operationId, { intent: JSON.stringify(intent), phase: 'imported', itemId: added[0].id });
    await guard();
    return receipt('Verified', added[0].id, 'Source imported and verified. Save your Premiere project to preserve the import.');
  } catch (error) {
    return receipt(mutationStarted ? 'UnknownOutcome' : 'Failed', null, String(error.message || error).slice(0, 1500));
  }
}

module.exports = { execute, pathKey, sameProject };
