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
const { frameBoundaryTicks, markerPositionTicks } = require('./range.js');

test('fractional frame boundaries restore only the truncated 100ns interval', () => {
  for (const frame of [10594584000n, 8475667200n, 10584000000n, 10160640000n]) {
    for (const index of [0n, 1n, 35n, 77n, 96n, 100000n]) {
      const exact = frame * index;
      const stored = exact * 5n / 127008n;
      assert.equal(frameBoundaryTicks(String(stored), String(frame)), String(exact));
      if (stored > 0n) assert.equal(frameBoundaryTicks(String(stored - 2n), String(frame)), nearestPremiereTicks(String(stored - 2n)));
      assert.equal(frameBoundaryTicks(String(stored + 1n), String(frame)), nearestPremiereTicks(String(stored + 1n)));
    }
  }
  assert.equal(frameBoundaryTicks('10000001', '10584000000'), '254016025402');
  assert.equal(frameBoundaryTicks('32115416', '10594584000'), '815782968000');
  for (const value of [null, '', '0', '-1', '1.5', '01', '9007199254740992'])
    assert.throws(() => frameBoundaryTicks('1', value), /frame duration/);
});

test('observed marker uses projected source minus projected native origin', () => {
  assert.equal(markerPositionTicks({ sourcePositionTicks: '40040000', positionTicks: '7924584' }, '10594584000'), '201297096000');
  assert.equal(markerPositionTicks({ sourcePositionTicks: '14597916', positionTicks: '14597916' }, '10594584000'), '370810440000');
  assert.equal(markerPositionTicks({ sourcePositionTicks: '40040000', positionTicks: '40040000' }, '10594584000'), '1017080064000');
});
