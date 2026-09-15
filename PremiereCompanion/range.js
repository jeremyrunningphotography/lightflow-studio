function nearestPremiereTicks(value) {
  if (typeof value !== 'string' || !/^\d+$/.test(value)) throw new Error('Lightflow In/Out values are invalid.');
  // .NET TimeSpan uses 10,000,000 ticks/s and Premiere uses 254,016,000,000 ticks/s.
  // Round to the closest Premiere tick; ties round up so retry/reconciliation stays deterministic.
  return ((BigInt(value) * 127008n + 2n) / 5n).toString();
}

module.exports = { nearestPremiereTicks };
