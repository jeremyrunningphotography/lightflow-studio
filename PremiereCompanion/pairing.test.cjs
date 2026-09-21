const { test } = require('node:test');
const assert = require('node:assert/strict');
const { PairingAccess, validatePairing, ENDPOINT, GRANT_KEY } = require('./pairing.js');
function fixture() {
  const data = new Map();
  const store = { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) };
  let value = { endpoint: ENDPOINT, protocol: 1, token: 'A'.repeat(64), expiresUtc: '2099-01-01T00:00:00Z' };
  let picks = 0, restores = 0;
  const folder = { getEntry: async name => { assert.equal(name, 'lightflow-pairing.json'); return { read: async () => JSON.stringify(value) }; } };
  const fs = {
    getFolder: async () => { picks++; return folder; },
    createPersistentToken: async entry => { assert.equal(entry, folder); return 'adobe-opaque-folder-grant'; },
    getEntryForPersistentToken: async token => { restores++; assert.equal(token, 'adobe-opaque-folder-grant'); return folder; }
  };
  return { fs, store, data, folder, access: new PairingAccess(fs, store), set: next => { value = next; },
    value: () => value, picks: () => picks, restores: () => restores };
}
test('first run requires a grant without opening a picker automatically; saved grant survives restart', async () => {
  const f = fixture(); assert.equal(await f.access.restore(), false); assert.equal(f.picks(), 0);
  assert.equal(f.access.needsGrant, true); assert.equal(await f.access.grant(), true);
  assert.deepEqual([...f.data.values()], ['adobe-opaque-folder-grant']);
  const restarted = new PairingAccess(f.fs, f.store);
  assert.equal(await restarted.restore(), true); assert.equal((await restarted.read()).token, f.value().token);
  assert.equal(f.picks(), 1); assert.equal(f.restores(), 1);
});
test('atomic credential replacement and reset use remembered folder without a new picker', async () => {
  const f = fixture(); await f.access.grant();
  f.set({ ...f.value(), token: 'B'.repeat(64) });
  assert.equal((await f.access.read()).token, 'B'.repeat(64)); assert.equal(f.picks(), 1);
  assert.equal(JSON.stringify([...f.data.values()]).includes('B'.repeat(64)), false);
});
test('expired and malformed credentials never authorize connection; renewal recovers automatically', async () => {
  const f = fixture(); await f.access.grant(); const valid = f.value();
  for (const invalid of [null, {}, { ...valid, endpoint: 'http://127.0.0.1:47857' },
    { ...valid, endpoint: ENDPOINT + '/extra' }, { ...valid, endpoint: 'https://example.com' },
    { ...valid, protocol: 2 }, { ...valid, token: 'short' }, { ...valid, expiresUtc: 'invalid' },
    { ...valid, expiresUtc: '2020-01-01' }]) {
    f.set(invalid); await assert.rejects(f.access.read());
  }
  f.set(valid); assert.equal((await f.access.read()).endpoint, ENDPOINT); assert.equal(f.picks(), 1);
});
test('cancel/wrong folder/storage failure never replaces a remembered grant', async () => {
  const f = fixture(); await f.access.grant();
  f.fs.getFolder = async () => null; assert.equal(await f.access.grant(), false);
  f.fs.getFolder = async () => ({ getEntry: async () => { throw Error('missing'); } });
  await assert.rejects(f.access.grant()); assert.equal(await f.access.restore(), true);
  const g = fixture(); g.store.setItem = () => { throw Error('storage unavailable'); };
  await assert.rejects(g.access.grant(), /storage unavailable/); assert.equal(g.access.folder, null);
});
test('invalid Adobe grant offers recovery; Forget removes remembered access and pause survives restart', async () => {
  const f = fixture(); await f.access.grant(); f.access.pause();
  const restarted = new PairingAccess(f.fs, f.store); assert.equal(restarted.paused, true);
  restarted.resume(); assert.equal(restarted.paused, false);
  f.fs.getEntryForPersistentToken = async () => { throw Error('revoked'); };
  assert.equal(await restarted.restore(), false); assert.equal(restarted.needsGrant, true);
  restarted.forget(); assert.equal(f.store.getItem(GRANT_KEY), null); assert.equal(await restarted.restore(), false);
});
test('forget during a pending picker cannot restore forgotten access', async () => {
  const f = fixture(); let current = true;
  f.fs.createPersistentToken = async () => { f.access.forget(); current = false; return 'opaque'; };
  assert.equal(await f.access.grant(() => current), false); assert.equal(f.store.getItem(GRANT_KEY), null);
});
test('credential expiry is strict at the expiry instant', () => {
  const f = fixture(); assert.throws(() => validatePairing(f.value(), Date.parse(f.value().expiresUtc)), /renew/);
});

test('profile switching requires an explicit folder grant and never reads another profile as fallback', async () => {
  const a = fixture(), b = fixture();
  b.set({ ...b.value(), token: 'B'.repeat(64) });
  await a.access.grant();
  const original = JSON.stringify(a.value());
  const selectedB = await b.fs.getFolder();
  a.fs.getFolder = async () => selectedB;
  a.fs.createPersistentToken = async entry => { assert.equal(entry, selectedB); return 'profile-b-grant'; };
  a.fs.getEntryForPersistentToken = async token => {
    assert.equal(token, 'profile-b-grant'); return selectedB;
  };
  // Making another profile available does not change the selected folder.
  assert.equal((await a.access.read()).token, 'A'.repeat(64));
  a.access.forget(); await a.access.grant();
  const restarted = new PairingAccess(a.fs, a.store);
  assert.equal(await restarted.restore(), true);
  assert.equal((await restarted.read()).token, 'B'.repeat(64));
  b.set({ ...b.value(), expiresUtc: '2020-01-01' });
  await assert.rejects(restarted.read(), /renew/);
  assert.equal(JSON.stringify(a.value()), original);
  restarted.forget();
  assert.equal(JSON.stringify(a.value()), original);
});

function panel(f) {
  const vm = require('node:vm'); const messages = [];
  const elements = Object.fromEntries(['pair', 'disconnect', 'status', 'forget', 'troubleshoot', 'recovery'].map(name => [name,
    { style: {}, addEventListener(_, handler) { this.click = handler; } }]));
  let pulse;
  const context = vm.createContext({
    require: name => name === 'uxp' ? { storage: { localFileSystem: f.fs }, host: { version: '26.5' }, versions: { uxp: '9.3' } }
      : name === 'premierepro' ? { Project: { getActiveProject: async () => null } } : require(name),
    localStorage: f.store, document: { getElementById: name => elements[name] },
    setInterval: callback => { pulse = callback; return 1; }, clearInterval: () => {},
    setTimeout: callback => { queueMicrotask(callback); },
    fetch: async (url, options) => { messages.push({ url, authorization: options.headers.Authorization });
      return { ok: true, status: url.endsWith('/poll') ? 204 : 200, json: async () => ({}) }; }
  });
  vm.runInContext(require('node:fs').readFileSync(require.resolve('./index.js'), 'utf8'), context);
  return { elements, messages, settle: () => new Promise(resolve => setImmediate(resolve)), pulse: () => pulse() };
}
test('production panel auto-connects on relaunch and rereads changed credentials without picker', async () => {
  const f = fixture(); await f.access.grant(); const p = panel(f); await p.settle();
  assert.match(p.elements.status.textContent, /Connected to Lightflow/); assert.equal(f.picks(), 1);
  assert.equal(p.messages[0].authorization, 'Bearer ' + 'A'.repeat(64));
  f.set({ ...f.value(), token: 'B'.repeat(64) }); await p.pulse();
  assert.equal(p.messages.at(-1).authorization, 'Bearer ' + 'B'.repeat(64));
  p.elements.disconnect.click(); const restarted = panel(f); await restarted.settle();
  assert.equal(restarted.messages.length, 0); assert.match(restarted.elements.status.textContent, /paused/);
  await restarted.elements.pair.click(); assert.match(restarted.elements.status.textContent, /Connected to Lightflow/);
  assert.equal(f.picks(), 1);
});
test('production panel waits for renewal without sending expired credentials, then recovers', async () => {
  const f = fixture(); await f.access.grant(); const valid = f.value(); f.set({ ...valid, expiresUtc: '2020-01-01' });
  const p = panel(f); await p.settle(); assert.equal(p.messages.length, 0); assert.match(p.elements.status.textContent, /renew/);
  f.set(valid); await p.pulse(); assert.match(p.elements.status.textContent, /Connected to Lightflow/); assert.equal(f.picks(), 1);
});
