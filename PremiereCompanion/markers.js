'use strict';
const { nearestPremiereTicks } = require('./range.js');
const pathKey = value => value.replace(/\\/g, '/').replace(/^\/\/\?\/(?=[a-z]:\/)/i, '').replace(/\/$/, '').toLowerCase();
const snapshot = marker => ({ guid: marker.guid, name: marker.name, startTicks: marker.startTicks,
  durationTicks: marker.durationTicks, type: marker.type, comments: marker.comments, colorIndex: marker.colorIndex });
const same = (a, b) => JSON.stringify(snapshot(a)) === JSON.stringify(snapshot(b));
const desired = (marker, state) => state.name === marker.name && state.startTicks === nearestPremiereTicks(marker.positionTicks)
  && state.durationTicks === '0' && state.type === 'Comment' && state.comments === '';
function validate(marker, source) {
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const keys = ['markerId', 'assetId', 'revision', 'name', 'sourcePositionTicks', 'positionTicks', 'subclipId', 'targetItemId', 'targetKey'];
  if (!marker || Object.keys(marker).length !== keys.length || Object.keys(marker).some(key => !keys.includes(key)))
    throw new Error('Unsupported point-marker contract fields.');
  if (!marker || !uuid.test(marker.markerId) || !uuid.test(marker.assetId) || marker.assetId !== source.assetId
      || !Number.isSafeInteger(marker.revision) || marker.revision < 1 || typeof marker.name !== 'string' || marker.name.length > 10000
      || !/^(0|[1-9][0-9]*)$/.test(marker.sourcePositionTicks) || !/^(0|[1-9][0-9]*)$/.test(marker.positionTicks)
      || BigInt(marker.positionTicks) > BigInt(marker.sourcePositionTicks)
      || BigInt(marker.sourcePositionTicks) > 9223372036854775807n
      || (marker.subclipId !== null && !uuid.test(marker.subclipId))
      || (!marker.subclipId && marker.positionTicks !== marker.sourcePositionTicks)
      || typeof marker.targetItemId !== 'string' || !marker.targetItemId || marker.targetItemId.length > 200
      || marker.targetKey !== (marker.subclipId ? `subclip:${marker.subclipId}` : `asset:${marker.assetId}`)
      || source.isSubclipPrerequisite) throw new Error('Invalid point-marker projection contract.');
}

async function executeMarker(command, adapter, journal, guard) {
  const intent = command.intent, marker = intent.marker;
  let mutationStarted = false;
  const receipt = (outcome, message, state = null) => ({ operationId: intent.operationId, outcome,
    itemId: marker?.targetItemId || null, message: 'Marker: ' + message, markerState: state, verification: outcome === 'Verified' ? 'point-marker-v1' : null });
  try {
    validate(marker, intent.source);
    await guard();
    const identity = JSON.stringify([intent.catalogId, intent.destinationId, marker.markerId, marker.assetId,
      marker.targetKey, marker.sourcePositionTicks, pathKey(intent.source.path)]);
    const saved = await journal.read(intent.operationId);
    if (saved && saved.identity !== identity) return receipt('Conflict', 'Marker projection identity changed.');
    let previous = saved?.state || command.previousReceipt?.markerState;
    const items = await adapter.items();
    const target = items.filter(item => item.id === marker.targetItemId && pathKey(item.mediaPath || '') === pathKey(intent.source.path));
    if (target.length !== 1) return receipt('Conflict', 'Mapped marker target is missing or relinked.');
    let current = await adapter.markers(marker.targetItemId);
    if (current.length > 999 || new Set(current.map(m => m.guid)).size !== current.length || current.some(m => !m.guid))
      return receipt('Conflict', 'Marker enumeration is ambiguous or exceeds the supported limit.');
    let mapped = previous && current.find(m => m.guid === previous.guid);
    let recreate = false;
    if (previous && previous.targetItemId !== marker.targetItemId) {
      if (items.some(item => item.id === previous.targetItemId)
          || current.some(m => !(command.knownMarkerGuids || []).includes(m.guid)))
        return receipt('Conflict', 'The marker target changed and cannot be safely reconciled.');
      mapped = null; recreate = true;
    } else if (previous && !mapped) {
      // No name/time adoption: new GUIDs may be editor replacements, so preserve them and stop.
      const known = new Set([...(previous.knownGuids || []), ...(command.knownMarkerGuids || [])]);
      if (!previous.knownGuids || current.some(m => !known.has(m.guid)))
        return receipt('Conflict', 'Deleted marker has an unmapped possible replacement; no duplicate was created.');
      recreate = true;
    }
    if (saved?.phase === 'mutating') {
      // Never infer ownership of a new marker after lost create/readback. A known GUID update
      // can be recovered only if all properties match its recorded desired post-state.
      if (!mapped || !saved.expected || (!same(mapped, saved.expected) && !same(mapped, previous)))
        return receipt('UnknownOutcome', 'Prior marker mutation is uncertain; duplicate creation is blocked.');
      previous = { ...mapped, targetItemId: marker.targetItemId, knownGuids: current.map(m => m.guid) };
    }
    if (mapped && (!previous || !same(mapped, previous)))
      return receipt('Conflict', 'Premiere marker properties changed; the editor marker was preserved.');
    if (!mapped && !recreate && (command.previouslyDispatched || saved)
        && !(command.previousReceipt?.outcome === 'Failed' && (!saved || saved.phase === 'ready')))
      return receipt('UnknownOutcome', 'Prior marker creation is uncertain; duplicate creation is blocked.');
    if (!mapped || !desired(marker, mapped)) {
      const expected = mapped ? { ...snapshot(mapped), name: marker.name, startTicks: nearestPremiereTicks(marker.positionTicks),
        durationTicks: '0', type: 'Comment', comments: '' } : null;
      // Durable pre-mutation evidence prevents unsafe replay after crashes/lost receipts.
      await journal.write(intent.operationId, { identity, phase: 'mutating', state: previous, expected });
      await guard();
      mutationStarted = true;
      await adapter.mutateMarker(marker.targetItemId, marker, mapped || null, current, guard);
      await guard();
      const after = await adapter.markers(marker.targetItemId);
      const candidates = mapped ? after.filter(m => m.guid === mapped.guid)
        : after.filter(m => !current.some(old => old.guid === m.guid));
      if (candidates.length !== 1 || !desired(marker, candidates[0])
          || (mapped && candidates[0].colorIndex !== mapped.colorIndex)
          || current.filter(m => !mapped || m.guid !== mapped.guid).some(old => !after.some(m => same(m, old))))
        return receipt('UnknownOutcome', 'Marker mutation could not be verified; automatic duplication is blocked.');
      mapped = candidates[0]; current = after;
    }
    const state = { ...snapshot(mapped), targetItemId: marker.targetItemId, knownGuids: current.map(m => m.guid) };
    await journal.write(intent.operationId, { identity, phase: 'verified', state });
    await guard();
    return receipt('Verified', 'Point marker verified.', state);
  } catch (error) {
    return receipt(error.markerConflict ? 'Conflict' : mutationStarted ? 'UnknownOutcome' : 'Failed', String(error.message || error).slice(0, 1500));
  }
}
module.exports = { executeMarker, same, snapshot, validate };
