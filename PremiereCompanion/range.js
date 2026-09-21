function nearestPremiereTicks(value) {
  if (typeof value !== 'string' || !/^\d+$/.test(value)) throw new Error('Lightflow In/Out values are invalid.');
  // .NET TimeSpan uses 10,000,000 ticks/s and Premiere uses 254,016,000,000 ticks/s.
  // Round to the closest Premiere tick; ties round up so retry/reconciliation stays deterministic.
  return ((BigInt(value) * 127008n + 2n) / 5n).toString();
}

function frameBoundaryTicks(value, frameTicks) {
  const nearest = BigInt(nearestPremiereTicks(value));
  if (typeof frameTicks !== 'string' || !/^[1-9][0-9]*$/.test(frameTicks)
      || BigInt(frameTicks) > BigInt(Number.MAX_SAFE_INTEGER))
    throw new Error('Premiere source frame duration is unavailable.');
  const frame = BigInt(frameTicks);
  const boundary = ((nearest + frame - 1n) / frame) * frame;
  // TimeSpan conversion loses up to one 100ns unit (including floating-point
  // step arithmetic). Restore only a frame boundary inside that interval, never quantize an arbitrary off-grid timestamp.
  const distance = boundary * 5n - BigInt(value) * 127008n;
  return (distance >= 0n && distance <= 127008n ? boundary : nearest).toString();
}

function markerPositionTicks(marker, frameTicks) {
  const origin = (BigInt(marker.sourcePositionTicks) - BigInt(marker.positionTicks)).toString();
  return (BigInt(frameBoundaryTicks(marker.sourcePositionTicks, frameTicks))
    - BigInt(frameBoundaryTicks(origin, frameTicks))).toString();
}

module.exports = { nearestPremiereTicks, frameBoundaryTicks, markerPositionTicks };
