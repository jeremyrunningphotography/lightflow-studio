const { test } = require('node:test');
const assert = require('node:assert/strict');
const { execute, sameProject } = require('./handoff.js');
const vm = require('node:vm');
const fsForPanel = require('node:fs');

test('panel allows only one folder picker and restores Connect after cancellation', async () => {
  const elements = Object.fromEntries(['pair', 'disconnect', 'status', 'forget', 'troubleshoot', 'recovery'].map(name => [name,
    { style: {}, addEventListener(_, handler) { this.click = handler; } }]));
  let calls = 0;
  let cancel;
  const uxp = { storage: { localFileSystem: { getFolder() {
    calls++;
    return new Promise(resolve => { cancel = resolve; });
  } } } };
  vm.runInNewContext(fsForPanel.readFileSync(require.resolve('./index.js'), 'utf8'), {
    require: name => name === 'uxp' ? uxp : name === 'premierepro' ? {} : require(name),
    document: { getElementById: name => elements[name] },
    localStorage: { getItem: () => null, setItem() {}, removeItem() {} }
  });
  await new Promise(resolve => setImmediate(resolve));
  const first = elements.pair.click();
  assert.equal(elements.pair.disabled, true);
  await elements.pair.click();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(calls, 1);
  cancel(null);
  await first;
  assert.equal(elements.pair.disabled, false);
  const next = elements.pair.click();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(calls, 2);
  cancel(null);
  await next;
});

function fixture() {
  const project = { guid: 'project-1', path: 'C:/test/edit.prproj', name: 'edit' };
  const command = { intent: { operationId: 'op-1', catalogId: 'catalog-1', destinationId: 'destination-1',
    project, binId: 'bin-1', createBinName: null, source: { assetId: 'asset-1', path: 'C:/test/source.mov' } },
    previouslyDispatched: false, previousReceipt: null };
  const stored = new Map();
  const items = [];
  let imports = 0;
  let subclips = 0;
  const adapter = {
    activeProject: async () => project, connected: () => true,
    items: async () => items.slice(), targetBin: async () => 'bin-1',
    importSource: async path => { imports++; items.push({ id: 'item-1', mediaPath: path }); },
    projectRange: async () => {}, clearRange: async () => {},
    createSubclip: async (sourceItemId, spec) => {
      const created = `subclip-${++subclips}`;
      items.push({ id: created, name: spec.name, mediaPath: items.find(item => item.id === sourceItemId)?.mediaPath });
      return created;
    }
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

test('native Subclip creation is identity-aware and an identical retry does not duplicate', async () => {
  const f = fixture();
  f.items.push({ id: 'source-item', name: 'source.mov', mediaPath: 'C:/test/source.mov' });
  f.command.intent.operationId = 'subclip-op';
  f.command.intent.subclip = { subclipId: 'subclip-id', name: 'Interview answer', revision: 3,
    sourceItemId: 'source-item', range: { inTicks: '10000001', outTicks: '30000002', sourceDurationTicks: '60000000' },
    isSourceFallback: false, hardBoundaries: true, takeVideo: true, takeAudio: true };
  const first = await execute(f.command, f.adapter, f.journal);
  assert.equal(first.outcome, 'Verified');
  assert.equal(f.items.filter(item => item.name === 'Interview answer').length, 1);
  const retry = { ...f.command, previouslyDispatched: true, previousReceipt: first };
  assert.equal((await execute(retry, f.adapter, f.journal)).outcome, 'Verified');
  assert.equal(f.items.filter(item => item.name === 'Interview answer').length, 1);
});

test('native Subclip retry preserves editor changes and conflicts on changed Lightflow projection', async () => {
  const f = fixture();
  f.items.push({ id: 'source-item', name: 'source.mov', mediaPath: 'C:/test/source.mov' });
  f.command.intent.operationId = 'subclip-op';
  f.command.intent.subclip = { subclipId: 'subclip-id', name: 'Original', revision: 1,
    sourceItemId: 'source-item', range: { inTicks: '10', outTicks: '90', sourceDurationTicks: '100' },
    isSourceFallback: false, hardBoundaries: true, takeVideo: true, takeAudio: true };
  const first = await execute(f.command, f.adapter, f.journal);
  f.items.find(item => item.id === first.itemId).name = 'Editor rename';
  assert.equal((await execute({ ...f.command, previouslyDispatched: true, previousReceipt: first }, f.adapter, f.journal)).outcome, 'Verified');
  const changed = structuredClone(f.command);
  changed.previouslyDispatched = true; changed.previousReceipt = first;
  changed.intent.subclip.name = 'Lightflow rename'; changed.intent.subclip.revision = 2;
  assert.equal((await execute(changed, f.adapter, f.journal)).outcome, 'Conflict');
});

test('unknown native Subclip outcome and unmapped same-name item never create duplicates', async () => {
  const f = fixture();
  f.items.push({ id: 'source-item', name: 'source.mov', mediaPath: 'C:/test/source.mov' });
  f.command.intent.operationId = 'subclip-op';
  f.command.intent.subclip = { subclipId: null, name: 'source', revision: 1, sourceItemId: 'source-item', range: null,
    isSourceFallback: true, hardBoundaries: true, takeVideo: true, takeAudio: true };
  assert.equal((await execute({ ...f.command, previouslyDispatched: true }, f.adapter, f.journal)).outcome, 'UnknownOutcome');
  f.items.push({ id: 'unmapped-subclip', name: 'source', mediaPath: 'C:/test/source.mov' });
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Conflict');
});

test('reported pre-mutation Subclip failure can retry without blocking independent work', async () => {
  const f = fixture();
  f.items.push({ id: 'source-item', name: 'source.mov', mediaPath: 'C:/test/source.mov' });
  f.command.intent.operationId = 'subclip-op';
  f.command.intent.subclip = { subclipId: 'subclip-id', name: 'Retry me', revision: 1,
    sourceItemId: 'source-item', range: { inTicks: '10', outTicks: '90', sourceDurationTicks: '100' },
    isSourceFallback: false, hardBoundaries: true, takeVideo: true, takeAudio: true };
  let connected = false;
  f.adapter.connected = () => connected;
  const failed = await execute(f.command, f.adapter, f.journal);
  assert.equal(failed.outcome, 'Failed');
  connected = true;
  const retry = { ...f.command, previouslyDispatched: true, previousReceipt: failed };
  assert.equal((await execute(retry, f.adapter, f.journal)).outcome, 'Verified');
  const independent = structuredClone(f.command);
  independent.intent.operationId = 'independent-op'; independent.intent.subclip.subclipId = 'independent-id';
  independent.intent.subclip.name = 'Independent';
  assert.equal((await execute(independent, f.adapter, f.journal)).outcome, 'Verified');
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
test('source In/Out is projected once and a lost receipt reconciles without a second mutation', async () => {
  const f = fixture();
  f.command.intent.source.range = { inTicks: '50000000', outTicks: '100000000', sourceDurationTicks: '150000000' };
  const applied = [];
  f.adapter.projectRange = async (itemId, range) => applied.push({ itemId, range });
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Verified');
  assert.deepEqual(applied, [{ itemId: 'item-1', range: f.command.intent.source.range }]);
  assert.equal(f.stored.get('op-1').phase, 'range-projected');
  assert.equal((await execute({ ...f.command, previouslyDispatched: true }, f.adapter, f.journal)).outcome, 'Verified');
  assert.equal(applied.length, 1);
  assert.equal(f.imports(), 1);
});
test('mutable range choices reconcile the same Catalog item without duplicate import', async () => {
  const f = fixture();
  f.command.intent.source.range = { inTicks: '50000000', outTicks: '100000000', sourceDurationTicks: '150000000' };
  const applied = []; const cleared = [];
  f.adapter.projectRange = async (itemId, range) => applied.push({ itemId, range });
  f.adapter.clearRange = async itemId => cleared.push(itemId);
  const first = await execute(f.command, f.adapter, f.journal);
  assert.equal(first.outcome, 'Verified');
  const changed = structuredClone(f.command);
  changed.previouslyDispatched = true; changed.previousReceipt = first;
  changed.intent.source.range = { inTicks: '60000000', outTicks: '110000000', sourceDurationTicks: '150000000' };
  assert.match((await execute(changed, f.adapter, f.journal)).message, /updated/);
  changed.intent.source.range = null;
  assert.match((await execute(changed, f.adapter, f.journal)).message, /cleared/);
  assert.equal(f.imports(), 1);
  assert.equal(applied.length, 2);
  assert.deepEqual(cleared, ['item-1']);
});
test('a changed destination bin is a permitted retry of the same operation', async () => {
  const f = fixture();
  const first = await execute(f.command, f.adapter, f.journal);
  const retry = structuredClone(f.command);
  retry.previouslyDispatched = true; retry.previousReceipt = first; retry.intent.binId = 'other-bin';
  assert.equal((await execute(retry, f.adapter, f.journal)).outcome, 'Verified');
  assert.equal(f.imports(), 1);
});
test('a new full-source import records its projection without a redundant clear', async () => {
  const f = fixture(); let clears = 0;
  f.adapter.clearRange = async () => { clears++; };
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Verified');
  assert.equal(clears, 0);
  assert.equal(f.stored.get('op-1').projection, 'full-source');
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
