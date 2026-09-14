const { test } = require('node:test');
const assert = require('node:assert/strict');
const { execute, sameProject } = require('./handoff.js');

function fixture() {
  const project = { guid: 'project-1', path: 'C:/test/edit.prproj', name: 'edit' };
  const command = { intent: { operationId: 'op-1', catalogId: 'catalog-1', destinationId: 'destination-1',
    project, binId: 'bin-1', createBinName: null, source: { assetId: 'asset-1', path: 'C:/test/source.mov' } },
    previouslyDispatched: false, previousReceipt: null };
  const stored = new Map();
  const items = [];
  let imports = 0;
  const adapter = {
    activeProject: async () => project, connected: () => true,
    items: async () => items.slice(), targetBin: async () => 'bin-1',
    importSource: async path => { imports++; items.push({ id: 'item-1', mediaPath: path }); }
  };
  const journal = { read: async id => stored.get(id), write: async (id, value) => stored.set(id, value) };
  return { command, adapter, journal, items, stored, imports: () => imports };
}
test('initial import is journaled and repeat verifies item identity without duplicate', async () => {
  const f = fixture();
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Verified');
  f.items[0].name = 'editor renamed'; f.items[0].parent = 'editor moved';
  assert.equal((await execute({ ...f.command, previouslyDispatched: true }, f.adapter, f.journal)).outcome, 'Verified');
  assert.equal(f.imports(), 1);
});
test('receipt survives companion data loss, but undo is a conflict', async () => {
  const f = fixture();
  const receipt = await execute(f.command, f.adapter, f.journal);
  f.stored.clear();
  const retry = { ...f.command, previouslyDispatched: true, previousReceipt: receipt };
  assert.equal((await execute(retry, f.adapter, f.journal)).outcome, 'Verified');
  f.items.length = 0;
  assert.equal((await execute(retry, f.adapter, f.journal)).outcome, 'Conflict');
  assert.equal(f.imports(), 1);
});
test('unknown crash gap never blindly replays, including an empty destination', async () => {
  const f = fixture();
  assert.equal((await execute({ ...f.command, previouslyDispatched: true }, f.adapter, f.journal)).outcome, 'UnknownOutcome');
  assert.equal(f.imports(), 0);
});
test('unmapped matching path is a conflict, never authoritative identity', async () => {
  const f = fixture(); f.items.push({ id: 'unmapped', mediaPath: 'c:\\test\\source.mov' });
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Conflict');
  assert.equal(f.imports(), 0);
});
test('relinked mapped item and copied payload are rejected', async () => {
  const f = fixture(); await execute(f.command, f.adapter, f.journal);
  f.items[0].mediaPath = 'C:/test/other.mov';
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Conflict');
  const changed = structuredClone(f.command); changed.intent.source.assetId = 'different-catalog-asset';
  assert.equal((await execute(changed, f.adapter, f.journal)).outcome, 'Conflict');
  assert.equal(f.imports(), 1);
});
test('active project switch during target discovery prevents source import', async () => {
  const f = fixture();
  f.adapter.targetBin = async () => { f.adapter.activeProject = async () => ({ guid: 'other', path: 'C:/other.prproj' }); return 'bin'; };
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Failed');
  assert.equal(f.imports(), 0);
});
test('Save As sharing a GUID is still a different destination', () => {
  assert.equal(sameProject({ guid: 'g', path: 'C:/copy.prproj' }, { guid: 'g', path: 'C:/original.prproj' }), false);
  assert.equal(sameProject({ guid: 'g', path: '\\\\?\\C:\\original.prproj' }, { guid: 'g', path: 'c:/original.prproj' }), true);
});
test('lost connection after import persists item for reconciliation', async () => {
  const f = fixture(); const importSource = f.adapter.importSource;
  f.adapter.importSource = async path => { await importSource(path); f.adapter.connected = () => false; };
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'UnknownOutcome');
  f.adapter.connected = () => true;
  assert.equal((await execute({ ...f.command, previouslyDispatched: true }, f.adapter, f.journal)).outcome, 'Verified');
  assert.equal(f.imports(), 1);
});
test('intent persistence failure prevents import; result persistence failure remains unknown', async () => {
  const f = fixture(); f.journal.write = async () => { throw new Error('disk full'); };
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Failed');
  assert.equal(f.imports(), 0);
  const g = fixture(); let writes = 0;
  g.journal.write = async () => { if (++writes === 2) throw new Error('disk full'); };
  assert.equal((await execute(g.command, g.adapter, g.journal)).outcome, 'UnknownOutcome');
});
test('ambiguous import readback is not success', async () => {
  const f = fixture(); f.adapter.importSource = async path => {
    f.items.push({ id: 'one', mediaPath: path }, { id: 'two', mediaPath: path });
  };
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'UnknownOutcome');
});
