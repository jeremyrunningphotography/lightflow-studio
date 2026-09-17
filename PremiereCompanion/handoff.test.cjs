const { test } = require('node:test');
const assert = require('node:assert/strict');
const { execute, sameProject } = require('./handoff.js');
const { createAdapter } = require('./adapter.js');
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
  let removals = 0;
  const adapter = {
    activeProject: async () => project, connected: () => true,
    items: async () => items.slice(), targetBin: async () => 'bin-1', prerequisiteSourceBin: async () => 'source-bin',
    existingTargetBin: async () => 'bin-1',
    subclipInBin: async itemId => (items.find(item => item.id === itemId)?.parent || 'bin-1') === 'bin-1',
    importSource: async (path, bin) => { imports++; items.push({ id: `item-${imports}`, mediaPath: path, parent: bin }); },
    projectRange: async () => {}, clearRange: async () => {},
    removeItem: async itemId => {
      const index = items.findIndex(item => item.id === itemId);
      if (index < 0) throw new Error('missing cleanup item');
      items.splice(index, 1); removals++;
    },
    createSubclip: async (sourceItemId, spec) => {
      const created = `subclip-${++subclips}`;
      items.push({ id: created, name: spec.name, mediaPath: items.find(item => item.id === sourceItemId)?.mediaPath, parent: 'bin-1' });
      return created;
    }
  };
  const journal = { read: async id => stored.get(id), write: async (id, value) => stored.set(id, value) };
  return { command, adapter, journal, items, stored, imports: () => imports, removals: () => removals };
}

function productionFixture(executeActions = true) {
  const root = folder('root', 'Root');
  const sourceBin = folder('source-bin', 'Existing sources');
  const targetBin = folder('target-bin', 'Selected destination');
  add(root, sourceBin); add(root, targetBin);
  const source = clip('source-item', 'source.mov', 'C:/test/source.mov', '1000');
  add(sourceBin, source);
  let created = 0;
  function folder(itemId, name) {
    return { itemId, name, children: [], getItems: async function () { return this.children.slice(); },
      createBinAction(childName) { return () => add(this, folder(`bin-${childName}`, childName)); },
      createMoveItemAction(item, destination) { return () => { item.parent.children = item.parent.children.filter(value => value !== item); add(destination, item); }; },
      createRemoveItemAction(item) { return () => { this.children = this.children.filter(value => value !== item); item.parent = null; }; } };
  }
  function clip(itemId, name, mediaPath, durationTicks) {
    return { itemId, name, mediaPath, async getMediaFilePath() { return this.mediaPath; },
      async getMedia() { return { getDuration: async () => ({ ticks: durationTicks }) }; },
      createSubClipAction(subclipName) { return () => add(source.parent,
        clip(`native-${++created}`, subclipName, mediaPath, durationTicks)); },
      createSetInOutPointsAction() { return () => {}; }, createClearInOutPointsAction() { return () => {}; } };
  }
  function add(parent, item) { item.parent = parent; parent.children.push(item); return item; }
  const walk = async bin => {
    const result = [];
    for (const item of await bin.getItems()) {
      result.push(item);
      if (item.children) result.push(...await walk(item));
    }
    return result;
  };
  const project = { guid: 'project-1', path: 'C:/test/edit.prproj', name: 'edit', getRootItem: async () => root,
    lockedAccess(callback) { callback(); }, executeTransaction(callback) {
      const actions = []; callback({ addAction: action => actions.push(action) });
      if (executeActions) for (const action of actions) action();
      return true;
    }, importFiles: async () => true };
  const ppro = { TickTime: { createWithTicks: ticks => ({ ticks: String(ticks) }) },
    Project: { getActiveProject: async () => project }, FolderItem: { cast: item => {
      if (!item.children) throw new Error('not a folder'); return item;
    } }, ClipProjectItem: { cast: item => {
      if (!item.mediaPath) throw new Error('not a clip'); return item;
    } }, ProjectItem: { cast: item => {
      item.getParentBin ||= async () => item.parent;
      return item;
    } } };
  const adapter = createAdapter(ppro, project, walk, item => item.itemId, value => String(value),
    { activeProject: async () => ({ guid: project.guid, path: project.path, name: project.name }), connected: () => true });
  return { adapter, project, source, sourceBin, targetBin, created: () => created };
}

test('production adapter executes native create and verifies selected-bin placement before success', async () => {
  const f = productionFixture();
  const result = await f.adapter.createSubclip('source-item', { name: 'Native', range: { inTicks: '10', outTicks: '90' },
    hardBoundaries: true, takeVideo: true, takeAudio: true }, f.targetBin);
  assert.equal(result, 'native-1');
  assert.equal(f.created(), 1);
  assert.deepEqual((await f.targetBin.getItems()).map(item => item.itemId), ['native-1']);
});

test('production adapter rejects a successful transaction result when no native mutation executes', async () => {
  const f = productionFixture(false);
  await assert.rejects(() => f.adapter.createSubclip('source-item', { name: 'No-op', range: { inTicks: '10', outTicks: '90' },
    hardBoundaries: true, takeVideo: true, takeAudio: true }, f.targetBin), /did not produce exactly one/);
  assert.equal(f.created(), 0);
});

test('production adapter removes a temporary prerequisite only after transaction readback', async () => {
  const f = productionFixture();
  await f.adapter.removeItem('source-item');
  assert.deepEqual((await f.sourceBin.getItems()).map(item => item.itemId), []);
  await assert.rejects(() => f.adapter.removeItem('source-item'), /unavailable for cleanup/);
});

test('production dispatch reports Verified only after the real native-create seam mutates and reads back', async () => {
  const f = productionFixture();
  const intent = { operationId: 'production-op', catalogId: 'catalog-1', destinationId: 'destination-1',
    project: { guid: 'project-1', path: 'C:/test/edit.prproj', name: 'edit' }, binId: 'target-bin', createBinName: null,
    source: { assetId: 'asset-1', path: 'C:/test/source.mov' }, subclip: { subclipId: 'subclip-1', name: 'Native',
      revision: 1, sourceItemId: 'source-item', range: { inTicks: '10', outTicks: '90', sourceDurationTicks: '1000' },
      isSourceFallback: false, hardBoundaries: true, takeVideo: true, takeAudio: true } };
  const stored = new Map();
  const result = await execute({ intent, previouslyDispatched: false, previousReceipt: null }, f.adapter,
    { read: async key => stored.get(key), write: async (key, value) => stored.set(key, value) });
  assert.equal(result.outcome, 'Verified');
  assert.equal(result.verification, 'native-subclip-v3');
  assert.equal(f.created(), 1);
  assert.deepEqual((await f.targetBin.getItems()).map(item => item.itemId), [result.itemId]);
});

test('production dispatch never converts a no-op native transaction into a successful receipt', async () => {
  const f = productionFixture(false);
  const intent = { operationId: 'production-no-op', catalogId: 'catalog-1', destinationId: 'destination-1',
    project: { guid: 'project-1', path: 'C:/test/edit.prproj', name: 'edit' }, binId: 'target-bin', createBinName: null,
    source: { assetId: 'asset-1', path: 'C:/test/source.mov' }, subclip: { subclipId: 'subclip-1', name: 'No-op',
      revision: 1, sourceItemId: 'source-item', range: { inTicks: '10', outTicks: '90', sourceDurationTicks: '1000' },
      isSourceFallback: false, hardBoundaries: true, takeVideo: true, takeAudio: true } };
  const stored = new Map();
  const result = await execute({ intent, previouslyDispatched: false, previousReceipt: null }, f.adapter,
    { read: async key => stored.get(key), write: async (key, value) => stored.set(key, value) });
  assert.notEqual(result.outcome, 'Verified');
  assert.equal(result.verification, null);
  assert.equal(f.created(), 0);
});
test('initial import is journaled and repeat verifies item identity without duplicate', async () => {
  const f = fixture();
  assert.equal((await execute(f.command, f.adapter, f.journal)).outcome, 'Verified');
  f.items[0].name = 'editor renamed'; f.items[0].parent = 'editor moved';
  assert.equal((await execute({ ...f.command, previouslyDispatched: true }, f.adapter, f.journal)).outcome, 'Verified');
  assert.equal(f.imports(), 1);
});

test('temporary Subclip source imports outside the destination and carries cleanup proof across retry', async () => {
  const f = fixture();
  f.command.intent.source.isSubclipPrerequisite = true;
  const first = await execute(f.command, f.adapter, f.journal);
  assert.equal(first.outcome, 'Verified');
  assert.equal(first.verification, 'temporary-subclip-source-v1');
  assert.equal(f.items[0].parent, 'source-bin');
  delete f.stored.get('op-1').temporaryPrerequisite; // Companion 1.1.4 journal compatibility.
  const retry = await execute({ ...f.command, previouslyDispatched: true, previousReceipt: null }, f.adapter, f.journal);
  assert.equal(retry.outcome, 'Verified');
  assert.equal(retry.verification, 'temporary-subclip-source-v1');
  assert.equal(f.imports(), 1);
});

test('native Subclip removes only a cleanup-proven temporary source after successful verification', async () => {
  const f = fixture();
  f.items.push({ id: 'source-item', name: 'source.mov', mediaPath: 'C:/test/source.mov', parent: 'source-bin' });
  f.command.intent.operationId = 'cleanup-op';
  f.command.intent.subclip = { subclipId: 'subclip-id', name: 'Clean result', revision: 1,
    sourceItemId: 'source-item', range: { inTicks: '10', outTicks: '90', sourceDurationTicks: '100' },
    isSourceFallback: false, hardBoundaries: true, takeVideo: true, takeAudio: true, removeSourceAfter: true };
  const result = await execute(f.command, f.adapter, f.journal);
  assert.equal(result.outcome, 'Verified');
  assert.equal(f.removals(), 1);
  assert.equal(f.items.some(item => item.id === 'source-item'), false);
  assert.equal(f.items.some(item => item.id === result.itemId), true);

  const preserved = fixture();
  preserved.items.push({ id: 'source-item', name: 'source.mov', mediaPath: 'C:/test/source.mov', parent: 'editor-bin' });
  preserved.command.intent.operationId = 'preserve-op';
  preserved.command.intent.subclip = { ...f.command.intent.subclip, name: 'Preserved result', removeSourceAfter: false };
  assert.equal((await execute(preserved.command, preserved.adapter, preserved.journal)).outcome, 'Verified');
  assert.equal(preserved.removals(), 0);
  assert.equal(preserved.items.some(item => item.id === 'source-item'), true);
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

test('verified native Subclip outside the selected destination is a conflict without moving or duplicating it', async () => {
  const f = productionFixture();
  const intent = { operationId: 'placement-op', catalogId: 'catalog-1', destinationId: 'destination-1',
    project: { guid: 'project-1', path: 'C:/test/edit.prproj', name: 'edit' }, binId: 'target-bin', createBinName: null,
    source: { assetId: 'asset-1', path: 'C:/test/source.mov' }, subclip: { subclipId: 'subclip-1', name: 'Native',
      revision: 1, sourceItemId: 'source-item', range: { inTicks: '10', outTicks: '90', sourceDurationTicks: '1000' },
      isSourceFallback: false, hardBoundaries: true, takeVideo: true, takeAudio: true } };
  const stored = new Map();
  const journal = { read: async key => stored.get(key), write: async (key, value) => stored.set(key, value) };
  const first = await execute({ intent, previouslyDispatched: false, previousReceipt: null }, f.adapter, journal);
  const native = (await f.targetBin.getItems())[0];
  f.targetBin.children = [];
  native.parent = f.sourceBin;
  f.sourceBin.children.push(native);

  const retry = await execute({ intent, previouslyDispatched: true, previousReceipt: first }, f.adapter, journal);

  assert.equal(retry.outcome, 'Conflict');
  assert.match(retry.message, /outside the selected destination/);
  assert.equal(f.created(), 1);
  assert.deepEqual((await f.targetBin.getItems()).map(item => item.itemId), []);
  assert.deepEqual((await f.sourceBin.getItems()).filter(item => item.itemId === first.itemId).map(item => item.itemId), [first.itemId]);
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
test('verified source deletion is recreated by an explicit retry without adopting path matches', async () => {
  const f = fixture();
  const receipt = await execute(f.command, f.adapter, f.journal);
  f.stored.clear();
  const retry = { ...f.command, previouslyDispatched: true, previousReceipt: receipt };
  assert.equal((await execute(retry, f.adapter, f.journal)).outcome, 'Verified');
  f.items.length = 0;
  const recreated = await execute(retry, f.adapter, f.journal);
  assert.equal(recreated.outcome, 'Verified');
  assert.notEqual(recreated.itemId, receipt.itemId);
  assert.equal(f.imports(), 2);
  f.items.length = 0;
  f.items.push({ id: 'unmapped-replacement', mediaPath: 'C:/test/source.mov' });
  assert.equal((await execute({ ...retry, previousReceipt: recreated }, f.adapter, f.journal)).outcome, 'Conflict');
  assert.equal(f.imports(), 2);
});

test('verified native Subclip deletion recreates the same durable operation without duplicating an unmapped candidate', async () => {
  const f = fixture();
  f.items.push({ id: 'source-item', name: 'source.mov', mediaPath: 'C:/test/source.mov' });
  f.command.intent.operationId = 'subclip-op';
  f.command.intent.subclip = { subclipId: 'subclip-id', name: 'Recover me', revision: 1,
    sourceItemId: 'source-item', range: { inTicks: '10', outTicks: '90', sourceDurationTicks: '100' },
    isSourceFallback: false, hardBoundaries: true, takeVideo: true, takeAudio: true };
  const first = await execute(f.command, f.adapter, f.journal);
  f.items.splice(f.items.findIndex(item => item.id === first.itemId), 1);
  const retry = { ...f.command, previouslyDispatched: true, previousReceipt: first };
  const recreated = await execute(retry, f.adapter, f.journal);
  assert.equal(recreated.outcome, 'Verified');
  assert.notEqual(recreated.itemId, first.itemId);
  assert.equal(f.items.filter(item => item.name === 'Recover me').length, 1);
  f.items.splice(f.items.findIndex(item => item.id === recreated.itemId), 1);
  f.items.push({ id: 'unmapped-native', name: 'Recover me', mediaPath: 'C:/test/source.mov', parent: 'bin-1' });
  const conflict = await execute({ ...retry, previousReceipt: recreated }, f.adapter, f.journal);
  assert.equal(conflict.outcome, 'Conflict');
  assert.match(conflict.message, /unmapped item|replacement Premiere item/);
  assert.equal(f.items.filter(item => item.name === 'Recover me').length, 1);
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
