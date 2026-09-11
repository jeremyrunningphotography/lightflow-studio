const assert = require('node:assert/strict');
const http = require('node:http');
const { once } = require('node:events');
const { createBridge } = require('./bridge.cjs');
(async () => {
  const server = createBridge('test-only-token');
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  const request = (headers = {}, method = 'GET', url = '/health') => new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port: server.address().port, path: url, method,
      headers: { Host: '127.0.0.1:47856', ...headers } }, res => {
      res.resume(); res.on('end', () => resolve(res.statusCode));
    });
    req.on('error', reject); req.end();
  });
  try {
    const auth = { Authorization: 'Bearer test-only-token' };
    assert.equal(await request(), 401);
    assert.equal(await request({ Authorization: 'Bearer wrong' }), 401);
    assert.equal(await request(auth), 200);
    assert.equal(await request({ ...auth, Origin: 'https://example.com' }), 403);
    assert.equal(await request({ ...auth, Host: 'attacker.example' }), 403);
    assert.equal(await request({ ...auth, 'Sec-Fetch-Site': 'cross-site' }), 403);
    assert.equal(await request(auth, 'POST', '/execute'), 404);
    assert.equal(await request(auth, 'GET', '/health?token=x'), 404);
    console.log('PASS: authenticated health, missing/wrong token, Origin, Host, browser metadata, method and path boundaries. Node only; not UXP proof.');
  } finally { server.close(); await once(server, 'close'); }
})().catch(error => { console.error(error); process.exitCode = 1; });
