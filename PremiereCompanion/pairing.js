'use strict';
const ENDPOINT = 'http://localhost:47857';
const GRANT_KEY = 'lightflow.connection-folder-grant.v1';
const PAUSED_KEY = 'lightflow.connection-paused.v1';

function validatePairing(value, now = Date.now()) {
  if (!value || value.endpoint !== ENDPOINT || value.protocol !== 1 ||
      typeof value.token !== 'string' || !/^[A-F0-9]{64}$/.test(value.token) ||
      typeof value.expiresUtc !== 'string' || !Number.isFinite(Date.parse(value.expiresUtc)))
    throw new Error('This folder does not contain valid Lightflow connection settings. Use Copy Setup Location in Lightflow.');
  if (Date.parse(value.expiresUtc) <= now)
    throw new Error('Waiting for Lightflow to renew the connection. Open Lightflow; it will reconnect automatically.');
  return value;
}

class PairingAccess {
  constructor(fs, store, now = Date.now) { this.fs = fs; this.store = store; this.now = now; this.folder = null; this.needsGrant = false; }
  get paused() { return this.store.getItem(PAUSED_KEY) === 'true'; }
  pause() { this.store.setItem(PAUSED_KEY, 'true'); }
  resume() { this.store.removeItem(PAUSED_KEY); }
  forget() { this.store.removeItem(GRANT_KEY); this.store.removeItem(PAUSED_KEY); this.folder = null; this.needsGrant = true; }
  async restore() {
    const token = this.store.getItem(GRANT_KEY);
    this.needsGrant = !token;
    if (!token) return false;
    try { this.folder = await this.fs.getEntryForPersistentToken(token); this.needsGrant = false; return true; }
    catch (_) { this.folder = null; this.needsGrant = true; return false; }
  }
  async readFolder(folder) {
    let text;
    try { text = await (await folder.getEntry('lightflow-pairing.json')).read(); }
    catch (_) { throw new Error('Waiting for Lightflow connection settings. Open Lightflow; use First-time setup if this is the wrong folder.'); }
    if (typeof text !== 'string' || text.length > 4096) throw new Error('Invalid Lightflow connection settings.');
    let value;
    try { value = JSON.parse(text); } catch (_) { throw new Error('Invalid Lightflow connection settings.'); }
    return validatePairing(value, this.now());
  }
  async read() {
    if (!this.folder) throw new Error('Allow Connection Access to complete one-time setup.');
    // A folder token survives Lightflow's atomic replacement of the credential file.
    return this.readFolder(this.folder);
  }
  async grant(allowCommit = () => true) {
    const folder = await this.fs.getFolder();
    if (!folder) return false;
    await this.readFolder(folder);
    const token = await this.fs.createPersistentToken(folder);
    if (!allowCommit()) return false;
    this.store.setItem(GRANT_KEY, token); // Persist Adobe's opaque grant, never the bearer credential.
    this.folder = folder; this.needsGrant = false; this.resume();
    return true;
  }
}
module.exports = { PairingAccess, validatePairing, ENDPOINT, GRANT_KEY, PAUSED_KEY };
