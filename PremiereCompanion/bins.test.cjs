const { test } = require('node:test');
const assert = require('node:assert/strict');
const { ProjectBins, enumerateBins } = require('./bins.js');
const project = { guid: 'one', path: 'C:/edit.prproj' };
const root = { id: 'root', name: 'root', getItems: async () => [] };
const folder = item => { if (!item.getItems) throw Error('not folder'); return item; };
const id = item => item.id;

test('empty project publishes its root; nested bins use native IDs and hierarchy, excluding media', async () => {
  assert.deepEqual(await enumerateBins(root, folder, id), [{ id: 'root', name: 'Project root' }]);
  const tree = { ...root, getItems: async () => [
    { id: 'clip', name: 'source.mov' },
    { id: 'a', name: 'Footage', getItems: async () => [{ id: 'b', name: 'Day 1', getItems: async () => [] }] },
    { id: 'c', name: 'Footage', getItems: async () => [] }
  ] };
  assert.deepEqual(await enumerateBins(tree, folder, id), [
    { id: 'root', name: 'Project root' }, { id: 'a', name: 'Footage' },
    { id: 'b', name: 'Footage / Day 1' }, { id: 'c', name: 'Footage' }
  ]);
});
test('busy heartbeat never publishes old bins for switched project, Save As, or no project', async () => {
  const cache = new ProjectBins();
  const bins = [{ id: 'a', name: 'original bin' }];
  await cache.read(project, true, async () => bins);
  assert.deepEqual(await cache.read(project, false), bins);
  assert.deepEqual(await cache.read({ ...project, path: 'C:/copy.prproj' }, false), []);
  await cache.read(project, true, async () => bins);
  assert.deepEqual(await cache.read({ ...project, guid: 'two' }, false), []);
  assert.deepEqual(await cache.read(null, false), []);
});
test('concurrent old enumeration cannot poison new project cache', async () => {
  const cache = new ProjectBins();
  let finish;
  const pending = cache.read(project, true, () => new Promise(resolve => { finish = resolve; }));
  const other = { ...project, guid: 'other' };
  await cache.read(other, false);
  finish([{ id: 'old', name: 'old' }]);
  await pending;
  assert.deepEqual(await cache.read(other, false), []);
});
test('rename/move refresh updates names and paths while keeping native IDs', async () => {
  const cache = new ProjectBins();
  await cache.read(project, true, async () => [{ id: 'a', name: 'original' }]);
  assert.deepEqual(await cache.read(project, true, async () => [{ id: 'a', name: 'Archive / renamed' }]),
    [{ id: 'a', name: 'Archive / renamed' }]);
});
test('enumeration failures and protocol limits are explicit errors, never partial dropdowns', async () => {
  await assert.rejects(enumerateBins({ ...root, getItems: async () => { throw Error('host unavailable'); } }, folder, id), /host unavailable/);
  await assert.rejects(enumerateBins({ ...root, getItems: async () => Array.from({ length: 1000 }, (_, i) => ({ ...root, id: String(i) })) }, folder, id), /1,000 bins/);
});

function panel(activeProject) {
  const vm = require('node:vm');
  const messages = [];
  const context = vm.createContext({
    require: name => name === 'premierepro' ? {
      Project: { getActiveProject: activeProject }, FolderItem: { cast: folder }
    } : name === 'uxp' ? { storage: { localFileSystem: {} }, host: { version: '26.5.0' }, versions: { uxp: '9.3.0' } } : require(name),
    document: { getElementById: () => ({ style: {}, addEventListener() {} }) },
    localStorage: { getItem: () => null, setItem() {}, removeItem() {} },
    fetch: async (_, options) => { messages.push(JSON.parse(options.body)); return { ok: true, status: 200, json: async () => ({}) }; }
  });
  vm.runInContext(require('node:fs').readFileSync(require.resolve('./index.js'), 'utf8'), context);
  vm.runInContext("running = true; access.read = async () => ({ endpoint: ENDPOINT, protocol: 1, expiresUtc: '2099-01-01', token: 'A'.repeat(64) });", context);
  return { messages, heartbeat: discover => vm.runInContext(`heartbeat(${discover})`, context) };
}
test('production heartbeat publishes root and clears prior bins on busy project switch', async () => {
  let current = { ...project, name: 'edit', getRootItem: async () => ({ ...root, getId: () => 'root' }) };
  const p = panel(async () => current);
  await p.heartbeat(true);
  assert.equal(p.messages[0].bins[0].id, 'root');
  current = { ...current, guid: 'new-project' };
  await p.heartbeat(false);
  assert.equal(p.messages[1].project.guid, 'new-project');
  assert.deepEqual(p.messages[1].bins, []);
  current = null; await p.heartbeat(false);
  assert.equal(p.messages[2].project, null); assert.deepEqual(p.messages[2].bins, []);
});
test('production heartbeat does not publish a snapshot if project switches during enumeration', async () => {
  let current = { ...project, name: 'edit', getRootItem: async () => {
    current = { ...current, guid: 'changed' };
    return { ...root, getId: () => 'root' };
  } };
  const p = panel(async () => current);
  await assert.rejects(p.heartbeat(true), /Active project changed/);
  assert.equal(p.messages.length, 0);
});
