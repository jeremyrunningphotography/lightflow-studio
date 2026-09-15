const assert = require('node:assert/strict');
const test = require('node:test');
const { nearestPremiereTicks } = require('./range.js');

test('nearest Premiere tick conversion is deterministic for every source-tick remainder', () => {
  assert.equal(nearestPremiereTicks('0'), '0');
  assert.equal(nearestPremiereTicks('5'), '127008');
  assert.equal(nearestPremiereTicks('1'), '25402');
  assert.equal(nearestPremiereTicks('2'), '50803');
  assert.equal(nearestPremiereTicks('3'), '76205');
  assert.equal(nearestPremiereTicks('4'), '101606');
});

test('nearest Premiere tick conversion rejects malformed source timestamps', () => {
  for (const value of ['', '-1', '1.5', 'abc', null])
    assert.throws(() => nearestPremiereTicks(value), /In\/Out values are invalid/);
});
