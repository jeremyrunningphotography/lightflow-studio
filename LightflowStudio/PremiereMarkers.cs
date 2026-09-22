using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace LightflowStudio;

// Exact authoritative time stays in 100ns units; all Premiere properties remain adapter projection state.
internal sealed record PremiereMarkerProjection(Guid MarkerId, Guid AssetId, long Revision, string Name,
    string SourcePositionTicks, string PositionTicks, Guid? SubclipId, string TargetItemId)
{
    public string TargetKey => SubclipId is { } id ? $"subclip:{id:D}" : $"asset:{AssetId:D}";
}
internal sealed record PremiereMarkerState(string Guid, string TargetItemId, string Name, string StartTicks,
    string DurationTicks, string Type, string Comments, int ColorIndex, string[] KnownGuids, string? FrameTicks = null);

internal static class PremiereMarkerTiming
{
    public static string Project(PremiereMarkerProjection marker, string? frameTicks)
    {
        if (frameTicks is null || !long.TryParse(frameTicks, NumberStyles.None, CultureInfo.InvariantCulture, out var frame)
            || frame <= 0 || frame > 9007199254740991L || frame.ToString(CultureInfo.InvariantCulture) != frameTicks)
            throw new InvalidOperationException("Marker receipt lacks valid source frame duration.");
        var source = BigInteger.Parse(marker.SourcePositionTicks, CultureInfo.InvariantCulture);
        var origin = source - BigInteger.Parse(marker.PositionTicks, CultureInfo.InvariantCulture);
        BigInteger Boundary(BigInteger value)
        {
            var nearest = (value * 127008 + 2) / 5;
            var boundary = (nearest + frame - 1) / frame * frame;
            var distance = boundary * 5 - value * 127008;
            return distance >= 0 && distance <= 127008 ? boundary : nearest;
        }
        return (Boundary(source) - Boundary(origin)).ToString(CultureInfo.InvariantCulture);
    }
}

internal static class PremiereMarkerPlanning
{
    public static IReadOnlyList<PremiereMarkerProjection> Plan(IEnumerable<TimelineMarker> markers, Guid assetId,
        string targetItemId, PremiereSubclipProjection? subclip = null)
    {
        long input = 0, output = long.MaxValue;
        if (subclip is { IsSourceFallback: false })
        {
            if (subclip.Range is null || !subclip.Range.TryGetMediaRange(out var range))
                throw new InvalidOperationException("A valid Subclip range is required for marker projection.");
            input = range!.EffectiveIn.Ticks; output = range.EffectiveOut.Ticks;
        }
        return markers.Where(marker => marker.AssetId == assetId && marker.Position.Ticks >= input
                && (subclip is not { IsSourceFallback: false } || marker.Position.Ticks < output))
            .OrderBy(marker => marker.Position).ThenBy(marker => marker.MarkerId)
            .Select(marker => new PremiereMarkerProjection(marker.MarkerId, marker.AssetId, marker.Revision, marker.Name,
                marker.Position.Ticks.ToString(CultureInfo.InvariantCulture),
                (marker.Position.Ticks - input).ToString(CultureInfo.InvariantCulture),
                subclip is { IsSourceFallback: false } ? subclip.SubclipId : null, targetItemId)).ToArray();
    }
}

internal sealed partial class CatalogPremiereHandoffs
{
    public async Task<IReadOnlyList<PremiereMarkerProjection>> PlanMarkersAsync(PremiereSource source, string itemId,
        PremiereSubclipProjection? subclip = null, CancellationToken token = default) =>
        PremiereMarkerPlanning.Plan(await new CatalogMarkerService(session).ListAsync(source.AssetId, token), source.AssetId, itemId, subclip);

    public Task<PremiereCommand> PrepareMarkerAsync(PremiereProject project, string binId, PremiereSource source,
        PremiereMarkerProjection marker, CancellationToken token = default) {
        return MutationLifecycle.RunAsync<PremiereCommand>(() => { return Task.Run(() =>
    {
        ValidateSource(source);
        if (source.IsSubclipPrerequisite || marker.AssetId != source.AssetId || marker.MarkerId == Guid.Empty
            || marker.AssetId == Guid.Empty || marker.SubclipId == Guid.Empty || marker.Revision < 1
            || marker.Name is null || marker.Name.Length > 10000 || string.IsNullOrWhiteSpace(marker.TargetItemId)
            || marker.TargetItemId.Length > 200 || !long.TryParse(marker.SourcePositionTicks, NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            || !long.TryParse(marker.PositionTicks, NumberStyles.None, CultureInfo.InvariantCulture, out var projected)
            || projected > position || (marker.SubclipId is null && projected != position)
            || string.IsNullOrWhiteSpace(project.Guid) || !System.IO.Path.IsPathFullyQualified(project.Path))
            throw new InvalidOperationException("Invalid point-marker projection.");
        var catalog = session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
        using var connection = catalog.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var destination = PremiereProtocol.DestinationId(project);
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT IntentJson,Dispatched,ReceiptJson FROM PremiereMarkerHandoffs WHERE DestinationId=$destination AND MarkerId=$marker AND TargetKey=$target";
        read.Parameters.AddWithValue("$destination", destination);
        read.Parameters.AddWithValue("$marker", marker.MarkerId.ToString("D"));
        read.Parameters.AddWithValue("$target", marker.TargetKey);
        PremiereIntent? prior = null;
        var dispatched = false;
        PremiereReceipt? receipt = null;
        using (var reader = read.ExecuteReader())
        {
            if (reader.Read())
            {
                prior = JsonSerializer.Deserialize<PremiereIntent>(reader.GetString(0), PremiereProtocol.Json)!;
                dispatched = reader.GetBoolean(1);
                receipt = reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<PremiereReceipt>(reader.GetString(2), PremiereProtocol.Json);
                if (prior.Source.AssetId != source.AssetId || prior.Marker!.SourcePositionTicks != marker.SourcePositionTicks)
                    throw new InvalidOperationException("Authoritative marker identity changed; reconcile before sending.");
            }
        }
        var intent = new PremiereIntent(prior?.OperationId ?? Guid.NewGuid(), catalog.Identity.CatalogId, destination,
            project, binId, null, source, Marker: marker) { CreatedUtc = prior?.CreatedUtc ?? DateTimeOffset.UtcNow };
        using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = prior is null
            ? "INSERT INTO PremiereMarkerHandoffs(OperationId,DestinationId,AssetId,MarkerId,TargetKey,IntentJson) VALUES($id,$destination,$asset,$marker,$target,$intent)"
            : "UPDATE PremiereMarkerHandoffs SET IntentJson=$intent WHERE OperationId=$id";
        write.Parameters.AddWithValue("$id", intent.OperationId.ToString());
        write.Parameters.AddWithValue("$destination", destination);
        write.Parameters.AddWithValue("$asset", source.AssetId.ToString());
        write.Parameters.AddWithValue("$marker", marker.MarkerId.ToString());
        write.Parameters.AddWithValue("$target", marker.TargetKey);
        write.Parameters.AddWithValue("$intent", JsonSerializer.Serialize(intent, PremiereProtocol.Json));
        write.ExecuteNonQuery();
        // Other markers projected later in a batch are proven Lightflow-owned, not ambiguous
        // replacements for a deleted mapping. Supply only verified GUID identities, never
        // similar names/positions or another marker's whole observed editor inventory.
        using var known = connection.CreateCommand();
        known.Transaction = transaction;
        known.CommandText = "SELECT ReceiptJson FROM PremiereMarkerHandoffs WHERE DestinationId=$destination AND TargetKey=$target AND ReceiptJson IS NOT NULL";
        known.Parameters.AddWithValue("$destination", destination);
        known.Parameters.AddWithValue("$target", marker.TargetKey);
        var knownGuids = new List<string>();
        using (var reader = known.ExecuteReader())
            while (reader.Read())
            {
                var priorReceipt = JsonSerializer.Deserialize<PremiereReceipt>(reader.GetString(0), PremiereProtocol.Json);
                if (priorReceipt is { Verification: "point-marker-v1" or "point-marker-v2", MarkerState: { } state } && state.TargetItemId == marker.TargetItemId)
                    knownGuids.Add(state.Guid);
            }
        transaction.Commit();
        return new PremiereCommand(intent, dispatched, receipt) { KnownMarkerGuids = knownGuids.Distinct().ToArray() };
    }, token); }, token);
    }

    private static void ValidateMarkerReceipt(PremiereMarkerProjection marker, PremiereReceipt receipt)
    {
        if (receipt.Outcome != PremiereOutcome.Verified)
        {
            if (receipt.MarkerState is not null)
                throw new InvalidOperationException("Only verified marker receipts may replace projection state.");
            return;
        }
        var state = receipt.MarkerState;
        var ticks = PremiereMarkerTiming.Project(marker, state?.FrameTicks);
        if (receipt.Verification != "point-marker-v2" || receipt.ItemId != marker.TargetItemId || state is null
            || string.IsNullOrWhiteSpace(state.Guid) || state.Guid.Length > 200 || state.TargetItemId != marker.TargetItemId
            || state.Name != marker.Name || state.StartTicks != ticks || state.DurationTicks != "0"
            || state.Type != "Comment" || state.Comments != "" || state.KnownGuids is null
            || !state.KnownGuids.Contains(state.Guid) || state.KnownGuids.Length > 1000
            || state.KnownGuids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 200))
            throw new InvalidOperationException("Marker receipt lacks exact verified projection state.");
    }
}
