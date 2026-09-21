'use strict';
const { executeMarker } = require('./markers.js');

// Platform-independent reconciliation policy. The adapter owns every Premiere API call.
const pathKey = value => value.replace(/\\/g, '/').replace(/^\/\/\?\/(?=[a-z]:\/)/i, '').replace(/\/$/, '').toLowerCase();
const sameProject = (left, right) => !!left && !!right && left.guid === right.guid && pathKey(left.path) === pathKey(right.path);
// A handoff operation is the Catalog source in one Premiere project. Bin placement and
// source In/Out are requested projections, so they must be reconciled rather than made
// part of an exactly-once operation identity.
const identity = intent => JSON.stringify({ catalogId: intent.catalogId, destinationId: intent.destinationId,
  source: { assetId: intent.source.assetId, path: pathKey(intent.source.path), sizeBytes: intent.source.sizeBytes,
    lastWriteUtcTicks: intent.source.lastWriteUtcTicks } });
const rangeKey = range => range ? `${range.inTicks}:${range.outTicks}:${range.sourceDurationTicks}` : 'full-source';
const savedIdentity = saved => {
  if (saved?.identity) return saved.identity;
  try { return saved?.intent ? identity(JSON.parse(saved.intent)) : null; } catch (_) { return null; }
};
const savedRangeKey = saved => {
  if (saved?.projection) return saved.projection;
  // Older journals recorded the intent but no projection key. Only their completed
  // range-projected phase proves that the legacy requested range was actually applied.
  try { return saved?.phase === 'range-projected' && saved.intent ? rangeKey(JSON.parse(saved.intent).source.range) : null; } catch (_) { return null; }
};
const savedTemporaryPrerequisite = saved => {
  if (saved?.temporaryPrerequisite === true) return true;
  try {
    return ['imported', 'range-projected'].includes(saved?.phase)
      && JSON.parse(saved.intent).source?.isSubclipPrerequisite === true;
  } catch (_) { return false; }
};
const subclipIdentity = intent => JSON.stringify({ source: JSON.parse(identity(intent)),
  subclip: intent.subclip.isSourceFallback ? `asset:${intent.source.assetId}` : `subclip:${intent.subclip.subclipId}` });
const subclipProjection = subclip => JSON.stringify({ name: subclip.name, revision: subclip.revision,
  range: rangeKey(subclip.range), hardBoundaries: subclip.hardBoundaries,
  takeVideo: subclip.takeVideo, takeAudio: subclip.takeAudio });
const temporarySubclipSourceVerification = 'temporary-subclip-source-v1';
const unmappedSourceConflict = 'Unmapped media already exists in this project. No automatic adoption or duplicate import.';

async function executeSubclip(command, adapter, journal, guard) {
  const intent = command.intent;
  const spec = intent.subclip;
  const projection = subclipProjection(spec);
  const receipt = (outcome, itemId, message) => ({ operationId: intent.operationId, outcome, itemId, message,
    projectionKey: projection, verification: outcome === 'Verified' ? 'native-subclip-v4' : null });
  let mutationStarted = false;
  try {
    await guard();
    const saved = await journal.read(intent.operationId);
    if (saved && saved.identity !== subclipIdentity(intent))
      return receipt('Conflict', saved.itemId || null, 'Catalog Subclip identity changed; no mutation performed.');
    const itemId = saved?.itemId || command.previousReceipt?.itemId;
    const projected = saved?.projection || command.previousReceipt?.projectionKey;
    const verified = ['native-subclip-v2', 'native-subclip-v3', 'native-subclip-v4'].includes(saved?.verification)
      || ['native-subclip-v2', 'native-subclip-v3', 'native-subclip-v4'].includes(command.previousReceipt?.verification);
    let recoverRemoved = false;
    let items = await adapter.items();
    const sources = items.filter(item => item.id === spec.sourceItemId);
    if (sources.length !== 1 || pathKey(sources[0].mediaPath || '') !== pathKey(intent.source.path))
      return receipt('Conflict', itemId || null, 'Mapped source is missing or relinked. Native Subclip creation was not attempted.');
    if (itemId) {
      const matches = items.filter(item => item.id === itemId);
      if (itemId === spec.sourceItemId || matches.length > 1)
        return receipt('Conflict', itemId, 'Mapped native Subclip is missing or relinked. Editor undo and edits are preserved; no duplicate was created.');
      if (projected && projected !== projection)
        return receipt('Conflict', itemId, 'The Lightflow Subclip changed after projection. The existing Premiere Subclip was preserved for review.');
      if (matches.length === 1) {
        if ((saved?.verification || command.previousReceipt?.verification) !== 'native-subclip-v4')
          return receipt('Conflict', itemId, 'This native Subclip predates corrected frame timing. It was preserved; send to a fresh project.');
        if (pathKey(matches[0].mediaPath || '') !== pathKey(intent.source.path))
          return receipt('Conflict', itemId, 'Mapped native Subclip was relinked. Restore its original media or delete it, then retry.');
        if (verified) {
          const targetBin = await adapter.existingTargetBin(intent.binId, intent.createBinName, guard);
          await guard();
          if (!targetBin || !await adapter.subclipInBin(itemId, targetBin))
            return receipt('Conflict', itemId, 'The mapped native Subclip is outside the selected destination. Editor organization was preserved; no item was moved or duplicated.');
        } else {
          if (!saved || saved.phase !== 'created')
            return receipt('Conflict', itemId, 'The earlier native Subclip receipt lacks production mutation proof. Recreate it in a fresh destination before retrying.');
          const targetBin = await adapter.targetBin(intent.binId, intent.createBinName, guard);
          await guard();
          await adapter.placeSubclip(itemId, targetBin);
        }
        if (spec.removeSourceAfter) await adapter.removeItem(spec.sourceItemId);
        await journal.write(intent.operationId, { identity: subclipIdentity(intent), phase: 'created', itemId, projection,
          verification: 'native-subclip-v4' });
        return receipt('Verified', itemId, 'Existing native Premiere Subclip verified; editor name and organization preserved.');
      }
      if (!verified)
        return receipt('Conflict', itemId, 'The missing native Subclip lacks verified creation proof. Remove any matching item and recreate it in a fresh destination.');
      if (items.some(item => item.name === spec.name && pathKey(item.mediaPath || '') === pathKey(intent.source.path)))
        return receipt('Conflict', itemId, 'A replacement Premiere item already has this Subclip name and source. Delete that unmapped item or restore the original mapped item, then retry.');
      recoverRemoved = true;
      await journal.write(intent.operationId, { identity: subclipIdentity(intent), phase: 'removed', previousItemId: itemId, projection,
        verification: 'native-subclip-v4' });
    }
    const reportedNoMutation = command.previousReceipt?.outcome === 'Failed' && !command.previousReceipt?.itemId;
    const safeFailedRetry = reportedNoMutation && (!saved || saved.phase === 'intent');
    if ((command.previouslyDispatched || saved) && !safeFailedRetry && !recoverRemoved)
      return receipt('UnknownOutcome', null, 'Prior native Subclip creation is uncertain. Inspect the original project; automatic duplication is blocked.');
    if (items.some(item => item.name === spec.name && pathKey(item.mediaPath || '') === pathKey(intent.source.path)))
      return receipt('Conflict', null, 'An unmapped Premiere item already has this Subclip name and source. Resolve it before retrying.');
    await journal.write(intent.operationId, { intent: JSON.stringify(intent), identity: subclipIdentity(intent), phase: 'intent', projection });
    await guard();
    const targetBin = await adapter.targetBin(intent.binId, intent.createBinName, guard);
    await guard();
    mutationStarted = true;
    const createdId = await adapter.createSubclip(spec.sourceItemId, spec, targetBin);
    items = await adapter.items();
    const matches = items.filter(item => item.id === createdId && pathKey(item.mediaPath || '') === pathKey(intent.source.path));
    if (matches.length !== 1) return receipt('UnknownOutcome', null, 'Native Subclip readback was ambiguous; reconcile before any retry.');
    if (spec.removeSourceAfter) await adapter.removeItem(spec.sourceItemId);
    await journal.write(intent.operationId, { identity: subclipIdentity(intent), phase: 'created', itemId: createdId, projection,
      verification: 'native-subclip-v4' });
    await guard();
    return receipt('Verified', createdId, 'Native Premiere Subclip created and verified. Save your Premiere project to preserve it.');
  } catch (error) {
    return receipt(mutationStarted ? 'UnknownOutcome' : 'Failed', null, String(error.message || error).slice(0, 1500));
  }
}

async function execute(command, adapter, journal) {
  const intent = command.intent;
  const receipt = (outcome, itemId, message, verification = null) =>
    ({ operationId: intent.operationId, outcome, itemId, message, verification });
  let mutationStarted = false;
  const guard = async () => {
    if (!sameProject(await adapter.activeProject(), intent.project))
      throw new Error('Active project changed. Return to the accepted project and reconcile.');
    if (!adapter.connected()) throw new Error('Connection expired. Reconnect before continuing.');
  };
  if (intent.marker) return executeMarker(command, adapter, journal, guard);
  if (intent.subclip) return executeSubclip(command, adapter, journal, guard);
  const reconcileRange = async (itemId, saved) => {
    // A later explicit Send may update or clear Lightflow's source-point projection. Editor
    // location and naming remain untouched, and the existing Catalog-mapped item is retained.
    if (intent.source.preserveRange) return false;
    const desired = rangeKey(intent.source.range);
    if (savedRangeKey(saved) === desired) return false;
    // A new import has Premiere's normal full-source state already. Record it without adding an
    // unnecessary second editor mutation; an existing mapped item still receives an explicit clear.
    if (!intent.source.range && saved?.phase === 'imported') {
      await journal.write(intent.operationId, { ...saved, identity: identity(intent), phase: 'range-projected', itemId, projection: desired });
      return false;
    }
    await guard();
    if (intent.source.range) await adapter.projectRange(itemId, intent.source.range);
    else await adapter.clearRange(itemId);
    await journal.write(intent.operationId, { ...saved, identity: identity(intent), phase: 'range-projected', itemId, projection: desired });
    await guard();
    return true;
  };
  try {
    await guard();
    const saved = await journal.read(intent.operationId);
    if (saved && savedIdentity(saved) !== identity(intent))
      return receipt('Conflict', saved.itemId || null, 'Catalog source identity changed; no mutation performed.');
    const itemId = saved?.itemId || command.previousReceipt?.itemId;
    let recoverRemoved = false;
    let items = await adapter.items();
    if (itemId) {
      const matches = items.filter(item => item.id === itemId);
      if (matches.length > 1)
        return receipt('Conflict', itemId, 'The mapped Premiere source identity is ambiguous. Remove duplicate mapped items, then retry.');
      if (matches.length === 1) {
        if (pathKey(matches[0].mediaPath || '') !== pathKey(intent.source.path))
          return receipt('Conflict', itemId, 'The mapped Premiere source was relinked. Restore its original media or delete it, then retry.');
        const updated = await reconcileRange(itemId, saved || { identity: identity(intent), phase: 'mapped', itemId });
        await guard();
        const temporary = savedTemporaryPrerequisite(saved)
          || command.previousReceipt?.verification === temporarySubclipSourceVerification;
        return receipt('Verified', itemId, updated
          ? (intent.source.range ? 'Existing source In/Out updated.' : 'Existing source In/Out cleared.')
          : 'Existing Catalog source verified; editor name and bin preserved.',
          temporary ? temporarySubclipSourceVerification : null);
      }
      const matching = items.filter(item => item.mediaPath && pathKey(item.mediaPath) === pathKey(intent.source.path));
      if (matching.length > 0)
        return receipt('Conflict', itemId, 'The mapped Premiere source was deleted, but an unmapped replacement uses the same media. Delete the replacement or restore the original mapped source, then retry.');
      const verified = command.previousReceipt?.outcome === 'Verified'
        || ['imported', 'range-projected', 'mapped'].includes(saved?.phase);
      if (!verified)
        return receipt('UnknownOutcome', itemId, 'The mapped source is missing without verified import proof. Inspect the project before retrying.');
      recoverRemoved = true;
      await journal.write(intent.operationId, { intent: JSON.stringify(intent), identity: identity(intent),
        phase: 'removed', previousItemId: itemId });
    }
    // There is no safe exactly-once import across two applications. Never infer identity from a path.
    const prior = command.previousReceipt;
    const blockedBeforeImport = saved?.phase === 'blocked-before-import'
      || (!saved && prior?.operationId === intent.operationId && prior.outcome === 'Conflict'
        && prior.itemId === null && prior.message === unmappedSourceConflict);
    if ((command.previouslyDispatched || saved) && !recoverRemoved && !blockedBeforeImport)
      return receipt('UnknownOutcome', null, 'Prior import outcome is uncertain. Inspect the original project; automatic reimport is blocked.');
    if (items.some(item => item.mediaPath && pathKey(item.mediaPath) === pathKey(intent.source.path))) {
      // Preserve positive evidence that no import began, even if the Conflict response is lost.
      // The intent journal below replaces this phase before any subsequent import can begin.
      await journal.write(intent.operationId, { identity: identity(intent), phase: 'blocked-before-import' });
      return receipt('Conflict', null, unmappedSourceConflict);
    }
    const before = new Set(items.map(item => item.id));
    await journal.write(intent.operationId, { intent: JSON.stringify(intent), identity: identity(intent), phase: 'intent' });
    await guard();
    const bin = intent.source.isSubclipPrerequisite
      ? await adapter.prerequisiteSourceBin(intent.binId, intent.createBinName, guard)
      : await adapter.targetBin(intent.binId, intent.createBinName, guard);
    await guard();
    mutationStarted = true;
    await adapter.importSource(intent.source.path, bin);
    items = await adapter.items();
    const added = items.filter(item => !before.has(item.id) && item.mediaPath
      && pathKey(item.mediaPath) === pathKey(intent.source.path));
    if (added.length !== 1) return receipt('UnknownOutcome', null, 'Import readback was ambiguous; reconcile before any retry.');
    const temporary = intent.source.isSubclipPrerequisite === true;
    const imported = { intent: JSON.stringify(intent), identity: identity(intent), phase: 'imported', itemId: added[0].id,
      temporaryPrerequisite: temporary };
    await journal.write(intent.operationId, imported);
    await reconcileRange(added[0].id, imported);
    await guard();
    return receipt('Verified', added[0].id, 'Source imported and verified. Save your Premiere project to preserve the import.',
      temporary ? temporarySubclipSourceVerification : null);
  } catch (error) {
    return receipt(mutationStarted ? 'UnknownOutcome' : 'Failed', null, String(error.message || error).slice(0, 1500));
  }
}

module.exports = { execute, pathKey, sameProject };
