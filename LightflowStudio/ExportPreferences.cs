using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LightflowStudio;

// Only reusable intent belongs here. Jobs, source selection, ranges and overwrite
// permission deliberately have no representation in this contract.
internal sealed record ExportLutPreference(ColorStagePolicyMode Mode = ColorStagePolicyMode.AsSelectedInLightflow,
    Guid? LutId = null, string? ContentSha256 = null, string? DisplayName = null);

internal sealed record ExportPreferences
{
    public ExportDestinationMode DestinationMode { get; init; } = ExportDestinationMode.SameFolderAsOriginal;
    public string? SpecificFolder { get; init; }
    public bool CreateSubfolder { get; init; } = true;
    public string SubfolderName { get; init; } = "Exports";
    public NamePart[] NameParts { get; init; } = [new(NamePartKind.OriginalName)];
    public NamePartSeparator Separator { get; init; } = NamePartSeparator.Hyphen;
    public ExportContainerChoice Container { get; init; } = ExportContainerChoice.SameAsSource;
    public ExportCodecChoice Codec { get; init; } = ExportCodecChoice.SameAsSource;
    public OutputResolution Resolution { get; init; } = OutputResolution.Source;
    public EncodingOptions Encoding { get; init; } = EncodingPresetCatalog.Recommended;
    public ExportLutPreference Camera { get; init; } = new();
    public ExportLutPreference Creative { get; init; } = new();

    internal static ExportPreferences Defaults(EncodingOptions baseline) => new()
    {
        Encoding = baseline with { FrameRate = 0, AudioMode = AudioEncodingMode.Copy }
    };

    internal void Validate()
    {
        if (!Enum.IsDefined(DestinationMode) || !Enum.IsDefined(Separator) || !Enum.IsDefined(Container)
            || !Enum.IsDefined(Codec) || !Enum.IsDefined(Resolution) || NameParts is null
            || NameParts.Any(p => p is null || !Enum.IsDefined(p.Kind)) || SubfolderName is null
            || Encoding is null || EncodingOptions.Normalize(Encoding) != Encoding
            || Camera is null || Creative is null || !ValidLut(Camera) || !ValidLut(Creative))
            throw new InvalidDataException("Saved Export preferences contain an unsupported or invalid value.");
        // Paths, empty recipes and cross-field incompatibilities are valid user intent;
        // current-context preflight diagnoses them, without replacing stored choices.
    }

    private static bool ValidLut(ExportLutPreference value) => Enum.IsDefined(value.Mode)
        && (value.Mode != ColorStagePolicyMode.Override || value.LutId.HasValue
            && !string.IsNullOrWhiteSpace(value.ContentSha256));
}

internal sealed class ExportPreferenceDocument
{
    public int SchemaVersion { get; set; } = 1;
    public EncodingOptions LegacyEncodingBaseline { get; set; } = EncodingPresetCatalog.Recommended;
    public JsonObject Overrides { get; set; } = new();
    public string[] RecentFolders { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

// A dialog owns one session. Loading and all writes run off the dispatcher. One
// coalescing pump bounds pending work; its lifetime is independent of preflight.
internal sealed class ExportPreferenceSession
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly string _path;
    private readonly object _gate = new();
    private ExportPreferenceDocument _document;
    private long _revision;
    private Task? _writer;
    private string? _fingerprint;
    public ExportPreferences Defaults { get; }
    public ExportPreferences Current { get; private set; }
    public IReadOnlyList<string> RecentFolders => _document.RecentFolders;
    public string? SaveError { get; private set; }
    public event Action? SaveStateChanged;

    private ExportPreferenceSession(string path, ExportPreferenceDocument document, string? fingerprint)
    {
        _path = path; _document = document; _fingerprint = fingerprint;
        Defaults = ExportPreferences.Defaults(document.LegacyEncodingBaseline);
        var values = JsonSerializer.SerializeToNode(Defaults, Json)!.AsObject();
        Merge(values, document.Overrides);
        Current = values.Deserialize<ExportPreferences>(Json) ?? throw new InvalidDataException("Empty Export preferences.");
        Current.Validate();
    }

    internal static async Task<ExportPreferenceSession> OpenAsync(string settingsPath) =>
        await Task.Run(() => Read(ExportDefaultsStore.PathFor(settingsPath)));

    private static ExportPreferenceSession Read(string path)
    {
        if (!File.Exists(path)) return new(path, new(), null);
        var bytes = File.ReadAllText(path);
        try
        {
            var node = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Empty Export preferences.");
            ExportPreferenceDocument document;
            if (node.ContainsKey(nameof(ExportPreferenceDocument.SchemaVersion)))
            {
                document = node.Deserialize<ExportPreferenceDocument>(Json) ?? throw new InvalidDataException("Empty Export preferences.");
                if (document.SchemaVersion != 1) throw new InvalidDataException("This Export preference schema is not supported by this version of Lightflow.");
            }
            else
            {
                // Legacy EncodingOptions used numeric enums. Never normalize corrupt
                // input into a replacement baseline without telling the user.
                var legacy = JsonSerializer.Deserialize<EncodingOptions>(bytes) ?? throw new InvalidDataException("Empty legacy Export defaults.");
                if (legacy != EncodingOptions.Normalize(legacy)) throw new InvalidDataException("Invalid legacy Export defaults.");
                document = new() { LegacyEncodingBaseline = legacy };
            }
            if (document.LegacyEncodingBaseline is null || document.Overrides is null || document.RecentFolders is null
                || document.LegacyEncodingBaseline != EncodingOptions.Normalize(document.LegacyEncodingBaseline)
                || document.RecentFolders.Any(p => string.IsNullOrWhiteSpace(p)))
                throw new InvalidDataException("Invalid Export preference document.");
            return new(path, document, bytes);
        }
        catch (JsonException ex) { throw new InvalidDataException("Saved Export preferences could not be read. The original file has been preserved.", ex); }
    }

    internal static async Task<ExportPreferenceSession> RecoverAsync(string settingsPath)
    {
        var path = ExportDefaultsStore.PathFor(settingsPath);
        return await Task.Run(() =>
        {
            // Called only after explicit destructive-recovery confirmation.
            if (File.Exists(path)) File.Move(path, path + $".recovery-{Guid.NewGuid():N}.json");
            return new ExportPreferenceSession(path, new(), null);
        });
    }

    public void Update(ExportPreferences value)
    {
        value.Validate();
        lock (_gate)
        {
            Current = value;
            var baseline = JsonSerializer.SerializeToNode(Defaults, Json)!;
            var node = JsonSerializer.SerializeToNode(value, Json)!;
            var sparse = Difference(node.AsObject(), baseline.AsObject());
            PreserveUnknown(_document.Overrides, sparse, baseline.AsObject());
            _document.Overrides = sparse;
            Schedule();
        }
    }

    public void CommitFolder(string path)
    {
        string canonical;
        try
        {
            if (!Path.IsPathFullyQualified(path.Trim())) return;
            canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        lock (_gate)
        {
            _document.RecentFolders = new[] { canonical }.Concat(_document.RecentFolders)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
            Schedule();
        }
    }

    public void Reset()
    {
        lock (_gate) _document.Overrides = new();
        Update(Defaults);
    }
    public bool HasOverrides => _document.Overrides.Count != 0;

    private void Schedule()
    {
        _revision++; SaveError = null;
        if (_writer is null || _writer.IsCompleted) _writer = Task.Run(WriteLoopAsync);
    }

    private async Task WriteLoopAsync()
    {
        while (true)
        {
            await Task.Delay(250).ConfigureAwait(false);
            long revision; string content;
            lock (_gate) { revision = _revision; content = JsonSerializer.Serialize(_document, Json); }
            try
            {
                // Detect another writer before replacing its file. The application
                // profile owns the normal process boundary; this also protects tools.
                var actual = File.Exists(_path) ? await File.ReadAllTextAsync(_path).ConfigureAwait(false) : null;
                if (actual != _fingerprint) throw new IOException("Export preferences changed outside this dialog. Reopen Export before saving more preferences.");
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, content).ConfigureAwait(false);
                    if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".backup");
                    else File.Move(temporary, _path);
                    _fingerprint = content;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                lock (_gate)
                {
                    SaveError = null;
                    if (revision == _revision) { _writer = null; break; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_gate) { SaveError = "Export preferences could not be saved: " + ex.Message; _writer = null; }
                break;
            }
        }
        SaveStateChanged?.Invoke();
    }

    public async Task FlushAsync()
    {
        while (true)
        {
            Task? writer;
            lock (_gate) writer = _writer;
            if (writer is null) return;
            await writer.ConfigureAwait(false);
        }
    }

    private static JsonObject Difference(JsonObject value, JsonObject baseline)
    {
        var result = new JsonObject();
        foreach (var pair in value)
        {
            if (JsonNode.DeepEquals(pair.Value, baseline[pair.Key])) continue;
            result[pair.Key] = pair.Value is JsonObject child && baseline[pair.Key] is JsonObject original
                ? Difference(child, original) : pair.Value?.DeepClone();
        }
        return result;
    }

    private static void Merge(JsonObject target, JsonObject changes)
    {
        foreach (var pair in changes)
            if (pair.Value is JsonObject child && target[pair.Key] is JsonObject original) Merge(original, child);
            else target[pair.Key] = pair.Value?.DeepClone();
    }

    private static void PreserveUnknown(JsonObject old, JsonObject changes, JsonObject known)
    {
        foreach (var pair in old)
        {
            if (!known.ContainsKey(pair.Key)) changes[pair.Key] = pair.Value?.DeepClone();
            else if (pair.Value is JsonObject child && known[pair.Key] is JsonObject original)
            {
                var nested = changes[pair.Key] as JsonObject ?? new();
                PreserveUnknown(child, nested, original);
                if (nested.Count != 0) changes[pair.Key] = nested;
            }
        }
    }
}
