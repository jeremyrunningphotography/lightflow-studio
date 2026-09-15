using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightflowStudio;

internal static class PremiereProtocol
{
    public const int Version = 1;
    public const int Port = 47857;
    public const string Endpoint = "http://localhost:47857";
    public const string CompanionVersion = "1.0.6";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string PathKey(string path) => Path.GetFullPath(path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
        ? @"\\" + path[8..] : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path)
        .TrimEnd('\\', '/').Replace('\\', '/').ToUpperInvariant();
    public static string DestinationId(PremiereProject project) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(project.Guid + "\n" + PathKey(project.Path))));
    public static bool SupportedHost(string version) => System.Version.TryParse(version, out var parsed)
        && parsed >= new Version(26, 5);
}

internal sealed record PremiereProject(string Guid, string Path, string Name);
internal sealed record PremiereBin(string Id, string Name);
internal sealed record PremiereHello(string InstanceId, string CompanionVersion, int Protocol,
    string HostVersion, string UxpVersion, PremiereProject? Project, IReadOnlyList<PremiereBin> Bins);
/// <summary>Lightflow ticks are 100ns. The companion maps them to the nearest Premiere tick.</summary>
internal sealed record PremiereRangeProjection(string InTicks, string OutTicks, string SourceDurationTicks)
{
    internal static bool TryCreate(MediaRange range, out PremiereRangeProjection? projection)
    {
        projection = null;
        if (range.IsFullSource || range.Validate().Count != 0) return false;
        projection = new(range.EffectiveIn.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            range.EffectiveOut.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            range.SourceDuration.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
    internal bool IsValid()
    {
        const System.Globalization.NumberStyles integer = System.Globalization.NumberStyles.None;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!System.Numerics.BigInteger.TryParse(InTicks, integer, culture, out var input)
            || !System.Numerics.BigInteger.TryParse(OutTicks, integer, culture, out var output)
            || !System.Numerics.BigInteger.TryParse(SourceDurationTicks, integer, culture, out var duration)) return false;
        var premiereInput = NearestPremiereTicks(input);
        var premiereOutput = NearestPremiereTicks(output);
        var premiereDuration = NearestPremiereTicks(duration);
        return input >= 0 && output > input && output <= duration && duration > 0
            && premiereOutput > premiereInput && premiereOutput <= premiereDuration;
    }

    private static System.Numerics.BigInteger NearestPremiereTicks(System.Numerics.BigInteger value) =>
        (value * 127008 + 2) / 5;

    internal bool TryGetMediaRange(out MediaRange? range)
    {
        range = null;
        const System.Globalization.NumberStyles integer = System.Globalization.NumberStyles.None;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!long.TryParse(InTicks, integer, culture, out var input) || !long.TryParse(OutTicks, integer, culture, out var output)
            || !long.TryParse(SourceDurationTicks, integer, culture, out var duration)) return false;
        var value = new MediaRange(TimeSpan.FromTicks(duration), TimeSpan.FromTicks(input), TimeSpan.FromTicks(output));
        if (value.Validate().Count != 0) return false;
        range = value;
        return true;
    }
}
internal sealed record PremiereSource(Guid AssetId, string Path, string SizeBytes, string LastWriteUtcTicks,
    PremiereRangeProjection? Range = null)
{
    [JsonIgnore]
    public string? RangeIssue { get; init; }
    [JsonIgnore]
    public string Name => System.IO.Path.GetFileName(Path);
    [JsonIgnore]
    public string RangeSummary => RangeIssue ?? (Range is null ? "Full source" : "Saved In/Out points");
    [JsonIgnore]
    public bool HasRange => Range is not null;
    [JsonIgnore]
    public bool HasRangeIssue => RangeIssue is not null;
    public PremiereSource WithoutRange() => this with { Range = null, RangeIssue = null };
}
internal sealed record PremiereIntent(Guid OperationId, Guid CatalogId, string DestinationId,
    PremiereProject Project, string BinId, string? CreateBinName, PremiereSource Source)
{
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
internal enum PremiereOutcome { Verified, Conflict, Failed, UnknownOutcome }
internal sealed record PremiereReceipt(Guid OperationId, PremiereOutcome Outcome, string? ItemId, string Message);
internal sealed record PremiereCommand(PremiereIntent Intent, bool PreviouslyDispatched, PremiereReceipt? PreviousReceipt)
{
    public Guid DispatchId { get; init; } = Guid.NewGuid();
}

/// <summary>Projection state lives in the existing Catalog, including its migrations, backup and relocation.</summary>
internal sealed class CatalogPremiereHandoffs(Func<CatalogDatabaseSession?> session)
{
    public Task<IReadOnlyList<PremiereCommand>> ListAsync() => Task.Run<IReadOnlyList<PremiereCommand>>(() =>
    {
        var catalog = session();
        if (catalog is null) return [];
        using var connection = catalog.OpenConnection();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT IntentJson,Dispatched,ReceiptJson FROM PremiereHandoffs ORDER BY rowid DESC LIMIT 500";
        using var reader = query.ExecuteReader();
        var results = new List<PremiereCommand>();
        while (reader.Read()) results.Add(new(JsonSerializer.Deserialize<PremiereIntent>(reader.GetString(0), PremiereProtocol.Json)!,
            reader.GetBoolean(1), reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<PremiereReceipt>(reader.GetString(2), PremiereProtocol.Json)));
        return results;
    });

    public Task<PremiereCommand> PrepareAsync(PremiereProject project, string binId, string? createBinName,
        PremiereSource source, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var catalog = session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
        if (source.AssetId == Guid.Empty || string.IsNullOrWhiteSpace(binId)
            || !Path.IsPathFullyQualified(project.Path) || string.IsNullOrWhiteSpace(project.Guid))
            throw new InvalidOperationException("A saved active project, target bin and Catalog asset are required.");
        ValidateSource(source);
        if (createBinName is not null && (string.IsNullOrWhiteSpace(createBinName) || createBinName.Length > 100
            || createBinName.IndexOfAny(['/', '\\', '\r', '\n']) >= 0))
            throw new InvalidOperationException("Enter a bin name of 1–100 characters without slashes.");
        using var connection = catalog.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var destination = PremiereProtocol.DestinationId(project);
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT IntentJson, Dispatched, ReceiptJson FROM PremiereHandoffs WHERE DestinationId=$destination AND AssetId=$asset";
        read.Parameters.AddWithValue("$destination", destination);
        read.Parameters.AddWithValue("$asset", source.AssetId.ToString());
        PremiereCommand result;
        using (var reader = read.ExecuteReader())
        {
            if (reader.Read())
            {
                var prior = JsonSerializer.Deserialize<PremiereIntent>(reader.GetString(0), PremiereProtocol.Json)!;
                // Changed source facts must never reuse an operation ID or silently relink editor media.
                if ((prior.Source with { Range = null, RangeIssue = null }) != (source with { Range = null, RangeIssue = null }))
                    throw new InvalidOperationException("Source changed since the previous handoff. Reconcile the existing Premiere item before sending again.");
                // Projection is part of an immutable handoff intent. A later Send selection reconciles the
                // original source-item projection rather than overwriting an editor's established item.
                result = new(prior, reader.GetBoolean(1), reader.IsDBNull(2) ? null
                    : JsonSerializer.Deserialize<PremiereReceipt>(reader.GetString(2), PremiereProtocol.Json));
                return result;
            }
        }
        var intent = new PremiereIntent(Guid.NewGuid(), catalog.Identity.CatalogId, destination, project,
            binId, createBinName, source);
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO PremiereHandoffs(OperationId,DestinationId,AssetId,IntentJson) VALUES($id,$destination,$asset,$intent)";
        insert.Parameters.AddWithValue("$id", intent.OperationId.ToString());
        insert.Parameters.AddWithValue("$destination", destination);
        insert.Parameters.AddWithValue("$asset", source.AssetId.ToString());
        insert.Parameters.AddWithValue("$intent", JsonSerializer.Serialize(intent, PremiereProtocol.Json));
        insert.ExecuteNonQuery();
        transaction.Commit();
        return new PremiereCommand(intent, false, null);
    }, cancellationToken);

    public Task MarkDispatchedAsync(PremiereIntent intent) => UpdateAsync(intent,
        "UPDATE PremiereHandoffs SET Dispatched=1 WHERE OperationId=$id", null);

    public Task SaveReceiptAsync(PremiereIntent intent, PremiereReceipt receipt)
    {
        if (receipt.OperationId != intent.OperationId || receipt.Message is null || receipt.Message.Length > 2000
            || !Enum.IsDefined(receipt.Outcome) || receipt.ItemId?.Length > 200
            || receipt.Outcome == PremiereOutcome.Verified && string.IsNullOrWhiteSpace(receipt.ItemId))
            throw new InvalidOperationException("Invalid companion receipt.");
        return UpdateAsync(intent, "UPDATE PremiereHandoffs SET ReceiptJson=$receipt WHERE OperationId=$id", receipt);
    }

    private Task UpdateAsync(PremiereIntent intent, string sql, PremiereReceipt? receipt) => Task.Run(() =>
    {
        var catalog = session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
        if (catalog.Identity.CatalogId != intent.CatalogId) throw new InvalidOperationException("The active Catalog changed.");
        using var connection = catalog.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", intent.OperationId.ToString());
        if (receipt is not null)
        {
            // A transient failure must not erase an established destination identity.
            using var prior = connection.CreateCommand();
            prior.CommandText = "SELECT ReceiptJson FROM PremiereHandoffs WHERE OperationId=$id";
            prior.Parameters.AddWithValue("$id", intent.OperationId.ToString());
            if (receipt.ItemId is null && prior.ExecuteScalar() is string json)
                receipt = receipt with { ItemId = JsonSerializer.Deserialize<PremiereReceipt>(json, PremiereProtocol.Json)?.ItemId };
            command.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, PremiereProtocol.Json));
        }
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Handoff intent is missing from the Catalog.");
    });

    public static void ValidateSource(PremiereSource source)
    {
        if (!Path.IsPathFullyQualified(source.Path) || source.Path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || source.Path.IndexOf(':', 2) >= 0 || !CompatibleExtension(Path.GetExtension(source.Path)))
            throw new InvalidOperationException("Select a supported source-media file at a filesystem location.");
        var file = new FileInfo(source.Path);
        if (!file.Exists || file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) != source.SizeBytes
            || file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) != source.LastWriteUtcTicks)
            throw new InvalidOperationException("Source is missing or changed. Refresh the Browser before sending.");
        if (source.Range is not null && !source.Range.IsValid())
            throw new InvalidOperationException("The saved In/Out range is too short for Premiere.");
    }

    public static bool CompatibleExtension(string extension) => extension.ToLowerInvariant() is
        ".mov" or ".mp4" or ".mxf" or ".avi" or ".mts" or ".m2ts" or ".wav" or ".mp3" or ".aif" or ".aiff"
        or ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff";
}
