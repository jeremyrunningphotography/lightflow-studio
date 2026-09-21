'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { execute } = require('./handoff.js');
const { createAdapter } = require('./adapter.js');
const { nearestPremiereTicks } = require('./range.js');
const assetId = '11111111-1111-4111-8111-111111111111';
const markerId = '22222222-2222-4222-8222-222222222222';
function fixture(frameTicks = 10584000000) {
  const project = { guid: 'project', path: 'C:/fixture/edit.prproj', name: 'edit' };
  const item = { getId: () => 'target', getFootageInterpretation: async () => ({ getFrameRate: () => 24 }), getMediaFilePath: async () => 'C:/fixture/source.mov' };
  const states = [], records = new Map();
  let next = 0, locked = false, mutations = 0, connected = true, noOp = false;
  let active = project;
  const action = fn => { assert.ok(locked); return () => { assert.ok(locked); fn(); }; };
  const native = state => ({ guid: state.guid, getName: () => state.name, getStart: () => ({ ticks: state.startTicks }),
    getDuration: () => ({ ticks: state.durationTicks }), getType: () => state.type, getComments: () => state.comments,
    getColorIndex: () => state.colorIndex, createSetNameAction: name => action(() => { state.name = name; mutations++; }) });
  const collection = { getMarkers: () => states.map(native),
    createAddMarkerAction: (name, type, start, duration, comments) => action(() => {
      mutations++;
      states.push({ guid: `guid-${++next}`, name, type, startTicks: start.ticks, durationTicks: duration.ticks, comments, colorIndex: 3 });
    }) };
  const host = { ...project, getRootItem: async () => ({}), lockedAccess: fn => { locked = true; try { fn(); } finally { locked = false; } },
    executeTransaction: fn => { assert.ok(locked); fn({ addAction: action => { if (!noOp) action(); } }); return true; } };
  const ppro = { FrameRate: { createWithValue: () => ({ ticksPerFrame: frameTicks }) }, Markers: { getMarkers: async () => collection }, ClipProjectItem: { cast: value => value },
    ProjectItem: { cast: value => value }, TickTime: { createWithTicks: ticks => ({ ticks }) } };
  const adapter = createAdapter(ppro, host, async () => [item], value => value.getId(), nearestPremiereTicks,
    { activeProject: async () => active, connected: () => connected });
  const command = { intent: { operationId: 'operation', catalogId: 'catalog', destinationId: 'destination', project,
    source: { assetId, path: 'C:/fixture/source.mov' }, marker: { markerId, assetId, revision: 1, name: '',
      sourcePositionTicks: '10000001', positionTicks: '10000001', subclipId: null, targetKey: `asset:${assetId}`, targetItemId: 'target' } },
    previouslyDispatched: false, previousReceipt: null };
  const journal = { read: async id => records.get(id), write: async (id, value) => records.set(id, structuredClone(value)) };
  return { command, adapter, journal, records, states, run: () => execute(command, adapter, journal),
    mutations: () => mutations, disconnect: () => connected = false, switchProject: () => active = { ...project, path: 'C:/other.prproj' },
    noOp: () => noOp = true };
}
test('production adapter creates zero-duration unnamed Comment marker at exact nearest tick; retry preserves GUID', async () => {
  const f = fixture(); const first = await f.run();
  assert.equal(first.outcome, 'Verified');
  assert.equal(first.markerState.startTicks, '254016025402');
  assert.equal(first.markerState.name, ''); assert.equal(first.markerState.durationTicks, '0');
  assert.equal(first.markerState.type, 'Comment'); assert.equal(first.markerState.comments, '');
  f.command.previouslyDispatched = true; f.command.previousReceipt = first;
  assert.equal((await f.run()).markerState.guid, first.markerState.guid); assert.equal(f.mutations(), 1);
});
test('Lightflow rename updates in place and preserves adapter default color', async () => {
  const f = fixture(); const first = await f.run();
  f.command.intent.marker.name = 'Renamed'; f.command.intent.marker.revision++;
  const result = await f.run(); assert.equal(result.outcome, 'Verified');
  assert.equal(result.markerState.guid, first.markerState.guid); assert.equal(result.markerState.colorIndex, 3);
  assert.equal(result.markerState.name, 'Renamed'); assert.equal(f.mutations(), 2);
});
for (const [property, value] of Object.entries({ name: 'Editor', startTicks: '42', durationTicks: '5', comments: 'Editor notes', colorIndex: 7, type: 'Chapter' })) {
  test(`editor ${property} mutation conflicts and survives resend`, async () => {
    const f = fixture(); await f.run(); f.states[0][property] = value;
    assert.equal((await f.run()).outcome, 'Conflict'); assert.equal(f.states[0][property], value); assert.equal(f.mutations(), 1);
  });
}
test('unrelated same-position/name marker is neither adopted nor changed', async () => {
  const f = fixture(); f.states.push({ guid: 'editor', name: '', startTicks: nearestPremiereTicks('10000001'), durationTicks: '0', type: 'Comment', comments: '', colorIndex: 3 });
  assert.equal((await f.run()).outcome, 'Verified'); assert.equal(f.states.length, 2); assert.equal(f.states[0].guid, 'editor');
});
test('deletion recreates only with verified GUID inventory and no new possible replacements', async () => {
  const f = fixture(); const first = await f.run(); f.states.length = 0;
  const restored = await f.run(); assert.equal(restored.outcome, 'Verified'); assert.notEqual(restored.markerState.guid, first.markerState.guid);
  f.states[0].guid = 'replacement'; assert.equal((await f.run()).outcome, 'Conflict'); assert.equal(f.mutations(), 2);
});
test('lost companion journal still reconciles Catalog marker GUID and properties', async () => {
  const f = fixture(); f.command.previousReceipt = await f.run(); f.command.previouslyDispatched = true; f.records.clear();
  assert.equal((await f.run()).outcome, 'Verified'); assert.equal(f.mutations(), 1);
});
test('deletion in a batch tolerates later durable Lightflow GUIDs but not unknown replacements', async () => {
  const f = fixture(); await f.run();
  f.states.push({ ...f.states[0], guid: 'later-lightflow' }); f.states.shift();
  f.command.knownMarkerGuids = ['later-lightflow'];
  assert.equal((await f.run()).outcome, 'Verified'); assert.equal(f.states.length, 2);
});
test('successfully executed no-op Action is not verification and retry cannot duplicate', async () => {
  const f = fixture(); f.noOp(); assert.equal((await f.run()).outcome, 'UnknownOutcome');
  assert.equal((await f.run()).outcome, 'UnknownOutcome'); assert.equal(f.states.length, 0);
});
test('lost create readback/journal cannot adopt a similar new marker', async () => {
  const f = fixture(); const write = f.journal.write;
  f.journal.write = async (id, value) => { if (value.phase === 'verified') throw Error('lost disk'); await write(id, value); };
  assert.equal((await f.run()).outcome, 'UnknownOutcome'); f.journal.write = write;
  assert.equal((await f.run()).outcome, 'UnknownOutcome'); assert.equal(f.states.length, 1);
});
test('known-GUID update can recover exact durable intended state after lost result', async () => {
  const f = fixture(); await f.run(); f.command.intent.marker.name = 'Rename';
  const write = f.journal.write;
  f.journal.write = async (id, value) => { if (value.phase === 'verified') throw Error('lost disk'); await write(id, value); };
  assert.equal((await f.run()).outcome, 'UnknownOutcome'); f.journal.write = write;
  assert.equal((await f.run()).outcome, 'Verified'); assert.equal(f.mutations(), 2);
});
test('unknown dispatched creation with no durable ownership never duplicates', async () => {
  const f = fixture(); f.command.previouslyDispatched = true;
  assert.equal((await f.run()).outcome, 'UnknownOutcome'); assert.equal(f.mutations(), 0);
});
test('connection loss after mutation reports unknown and verified journal permits safe retry', async () => {
  const f = fixture(); const mutate = f.adapter.mutateMarker;
  f.adapter.mutateMarker = async (...args) => { await mutate(...args); f.disconnect(); };
  assert.equal((await f.run()).outcome, 'UnknownOutcome'); assert.equal(f.mutations(), 1);
});
for (const action of ['disconnect', 'switchProject']) test(`${action} blocks all mutation`, async () => {
  const f = fixture(); f[action](); assert.equal((await f.run()).outcome, 'Failed'); assert.equal(f.mutations(), 0);
});
test('late editor mutation between discovery and locked Action is preserved', async () => {
  const f = fixture(); await f.run(); f.command.intent.marker.name = 'Rename';
  const mutate = f.adapter.mutateMarker;
  f.adapter.mutateMarker = async (...args) => { f.states[0].name = 'late editor'; await mutate(...args); };
  assert.equal((await f.run()).outcome, 'Conflict'); assert.equal(f.states[0].name, 'late editor');
  f.adapter.mutateMarker = mutate; f.states[0].name = '';
  assert.equal((await f.run()).outcome, 'Verified'); assert.equal(f.states[0].name, 'Rename');
});
for (const change of [m => m.positionTicks = '0.1', m => m.positionTicks = '-1', m => m.assetId = 'wrong',
  m => m.targetKey = 'wrong', m => m.revision = 0]) test('invalid marker contract fails before mutation: ' + change, async () => {
  const f = fixture(); change(f.command.intent.marker); assert.equal((await f.run()).outcome, 'Failed'); assert.equal(f.mutations(), 0);
});
test('temporary prerequisite is never a marker target', async () => {
  const f = fixture(); f.command.intent.source.isSubclipPrerequisite = true;
  assert.equal((await f.run()).outcome, 'Failed'); assert.equal(f.mutations(), 0);
});
test('Subclip-relative input is converted only at the production TickTime boundary', async () => {
  const f = fixture(), m = f.command.intent.marker;
  m.subclipId = '33333333-3333-4333-8333-333333333333'; m.targetKey = `subclip:${m.subclipId}`;
  m.sourcePositionTicks = '120000001'; m.positionTicks = '20000001';
  assert.equal((await f.run()).markerState.startTicks, nearestPremiereTicks('20000001'));
});
test('extended Windows media paths reconcile through the established path identity spelling', async () => {
  const f = fixture(); f.command.intent.source.path = '\\\\?\\C:\\fixture\\source.mov';
  assert.equal((await f.run()).outcome, 'Verified');
});
test('recreated targets permit only other durably mapped marker GUIDs', async () => {
  const f = fixture(); const first = await f.run();
  f.records.clear(); f.command.previousReceipt = { ...first, markerState: { ...first.markerState, targetItemId: 'removed-target' } };
  f.command.previouslyDispatched = true; f.states[0].guid = 'other-owned';
  f.command.knownMarkerGuids = ['other-owned'];
  const result = await f.run(); assert.equal(result.outcome, 'Verified'); assert.equal(f.states.length, 2);
});
test('extra Premiere domain fields are rejected rather than expanding point-marker scope', async () => {
  const f = fixture(); f.command.intent.marker.duration = '1';
  assert.equal((await f.run()).outcome, 'Failed'); assert.equal(f.mutations(), 0);
});

for (const [source, relative, expected, subclip] of [
  ['40040000', '7924584', '201297096000', true],
  ['14597916', '14597916', '370810440000', true],
  ['14597916', '14597916', '370810440000', false],
  ['40040000', '40040000', '1017080064000', false]
]) test(`production marker frame timing: source ${source}, relative ${relative}, subclip ${subclip}`, async () => {
  const f = fixture(10594584000);
  Object.assign(f.command.intent.marker, { sourcePositionTicks: source, positionTicks: relative,
    subclipId: subclip ? markerId : null, targetKey: subclip ? `subclip:${markerId}` : `asset:${assetId}` });
  const result = await f.run();
  assert.equal(result.outcome, 'Verified'); assert.equal(result.verification, 'point-marker-v2');
  assert.equal(result.markerState.startTicks, expected); assert.equal(result.markerState.frameTicks, '10594584000');
  assert.equal((await f.run()).markerState.guid, result.markerState.guid); assert.equal(f.mutations(), 1);
});

test('old mapped marker timing is preserved with conflict before mutation', async () => {
  const f = fixture(10594584000);
  Object.assign(f.command.intent.marker, { sourcePositionTicks: '14597916', positionTicks: '14597916' });
  const first = await f.run(); f.records.clear();
  f.states[0].startTicks = nearestPremiereTicks('14597916');
  f.command.previousReceipt = { ...first, verification: 'point-marker-v1', markerState: { ...first.markerState, ...f.states[0], frameTicks: undefined } };
  const result = await f.run(); assert.equal(result.outcome, 'Conflict');
  assert.match(result.message, /fresh project/); assert.equal(f.mutations(), 1);
  assert.equal(f.states[0].startTicks, '370810423066');
});

test('unavailable source frame duration blocks marker creation', async () => {
  const f = fixture(NaN); assert.equal((await f.run()).outcome, 'Failed'); assert.equal(f.mutations(), 0);
});
