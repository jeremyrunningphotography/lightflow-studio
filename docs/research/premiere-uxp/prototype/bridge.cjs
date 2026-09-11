// Disposable loopback reachability experiment, not the production handoff service.
const http = require('node:http');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

function createBridge(token) {
  return http.createServer((request, response) => {
    const reply = (status, payload) => {
      response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
      response.end(JSON.stringify(payload));
    };
    if (request.headers.host !== '127.0.0.1:47856' || request.headers.origin ||
        (request.headers['sec-fetch-site'] && request.headers['sec-fetch-site'] !== 'none'))
      return reply(403, { error: 'browser-or-host-rejected' });
    const supplied = Buffer.from(request.headers.authorization || '');
    const expected = Buffer.from(`Bearer ${token}`);
    if (supplied.length !== expected.length || !crypto.timingSafeEqual(supplied, expected))
      return reply(401, { error: 'unauthorized' });
    if (request.method !== 'GET' || request.url !== '/health')
      return reply(404, { error: 'unsupported-command' });
    return reply(200, { protocolVersion: 1, prototype: true, capability: 'health-only' });
  });
}
module.exports = { createBridge };
if (require.main === module) {
  const folder = fs.realpathSync(process.argv[2]);
  const fixture = JSON.parse(fs.readFileSync(path.join(folder, 'fixture.json'), 'utf8'));
  if (fixture.purpose !== 'Lightflow issue 256 disposable generated media') throw Error('Not a fixture folder');
  const token = crypto.randomBytes(32).toString('hex');
  const server = createBridge(token);
  server.requestTimeout = 5000;
  server.headersTimeout = 5000;
  server.on('error', error => { console.error(error.message); process.exitCode = 1; });
  server.listen(47856, '127.0.0.1', () => {
    fs.writeFileSync(path.join(folder, 'pairing.json'), JSON.stringify({ token }), { flag: 'wx', mode: 0o600 });
    console.log('Disposable health bridge listening on 127.0.0.1:47856 for 20 minutes. Token is not logged.');
  });
  const stop = () => {
    if (server.listening) server.close();
    const pairing = path.join(folder, 'pairing.json');
    if (fs.existsSync(pairing) && JSON.parse(fs.readFileSync(pairing, 'utf8')).token === token) fs.unlinkSync(pairing);
  };
  setTimeout(stop, 20 * 60 * 1000).unref();
  process.on('SIGINT', stop);
  process.on('SIGTERM', stop);
}
