using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum LutDimension { ThreeDimensional }
internal enum LutResourceAvailability { Available, Missing }
internal sealed record LutValidationResult(bool IsValid, LutDimension? Dimension = null, int? Size = null, string? Diagnostic = null);
internal sealed record ManagedLutResource(Guid LutId, string DisplayName, string OriginalFileName,
    string ContentSha256, LutDimension Dimension, int Size, LutResourceAvailability Availability,
    string? FilePath = null, string? Diagnostic = null);
internal sealed record LutFolderProblem(string FileName, string Diagnostic);
internal sealed record LutLibrarySnapshot(string Folder, IReadOnlyList<ManagedLutResource> Resources,
    IReadOnlyList<LutFolderProblem> Problems);

internal sealed record CubeLutData(int Size, float[] Samples)
{
    public System.Numerics.Vector3 DomainMin { get; init; } = System.Numerics.Vector3.Zero;
    public System.Numerics.Vector3 DomainMax { get; init; } = System.Numerics.Vector3.One;

    public static CubeLutData Load(string path)
    {
        var content = File.ReadAllBytes(path);
        var validation = CubeLutValidator.Validate(content);
        if (!validation.IsValid) throw new InvalidDataException(validation.Diagnostic);
        var samples = new List<float>(checked(validation.Size!.Value * validation.Size.Value * validation.Size.Value * 4));
        var text = new UTF8Encoding(false, true).GetString(content);
        var domainMin = System.Numerics.Vector3.Zero;
        var domainMax = System.Numerics.Vector3.One;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0) continue;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 4 && fields[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase))
            { domainMin = new(Parse(fields[1]), Parse(fields[2]), Parse(fields[3])); continue; }
            if (fields.Length == 4 && fields[0].Equals("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            { domainMax = new(Parse(fields[1]), Parse(fields[2]), Parse(fields[3])); continue; }
            if (fields.Length != 3 || !fields.All(field => float.TryParse(field, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _))) continue;
            samples.Add(float.Parse(fields[0], CultureInfo.InvariantCulture));
            samples.Add(float.Parse(fields[1], CultureInfo.InvariantCulture));
            samples.Add(float.Parse(fields[2], CultureInfo.InvariantCulture));
            samples.Add(1);
        }
        if (domainMax.X <= domainMin.X || domainMax.Y <= domainMin.Y || domainMax.Z <= domainMin.Z)
            throw new InvalidDataException("DOMAIN_MAX must be greater than DOMAIN_MIN on every channel.");
        return new(validation.Size.Value, samples.ToArray()) { DomainMin = domainMin, DomainMax = domainMax };
        static float Parse(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

internal sealed record PlayerColorPipeline(CubeLutData? Camera, CubeLutData? Creative)
{
    public bool HasColor => Camera is not null || Creative is not null;
}

internal static class CubeLutValidator
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static LutValidationResult Validate(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty) return Invalid("The file is empty.");
        if (content.Length > MaximumBytes) return Invalid("The file is larger than the supported 16 MB limit.");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(content); }
        catch (DecoderFallbackException) { return Invalid("The file is not valid UTF-8 text."); }

        var size = 0;
        var declared = false;
        var values = 0L;
        double[]? domainMin = null, domainMax = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0) continue;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields[0].Equals("TITLE", StringComparison.OrdinalIgnoreCase)) continue;
            if (fields[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase)
                || fields[0].Equals("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                if (fields.Length != 4 || !fields.Skip(1).All(IsFiniteNumber))
                    return Invalid($"{fields[0]} must contain three finite numbers.");
                var parsed = fields.Skip(1).Select(field => double.Parse(field, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                if (fields[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase)) domainMin = parsed;
                else domainMax = parsed;
                continue;
            }
            if (fields[0].Equals("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
                return Invalid("1D LUTs are not supported; use a 3D .cube LUT.");
            if (fields[0].Equals("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                if (declared || fields.Length != 2 || !int.TryParse(fields[1], NumberStyles.None,
                        CultureInfo.InvariantCulture, out size))
                    return Invalid("The file must contain exactly one valid LUT_3D_SIZE declaration.");
                declared = true;
                if (size is < 2 or > 256) return Invalid("The declared 3D LUT size must be between 2 and 256.");
                continue;
            }
            if (fields.Length != 3 || !fields.All(IsFiniteNumber))
                return Invalid("Each LUT data row must contain three finite numbers.");
            values++;
        }
        if (!declared) return Invalid("The file is missing a LUT_3D_SIZE declaration.");
        domainMin ??= [0, 0, 0];
        domainMax ??= [1, 1, 1];
        if (Enumerable.Range(0, 3).Any(index => domainMax[index] <= domainMin[index]))
            return Invalid("DOMAIN_MAX must be greater than DOMAIN_MIN on every channel.");
        var expected = checked((long)size * size * size);
        if (values != expected) return Invalid($"The LUT declares {expected:N0} data rows but contains {values:N0}.");
        return new(true, LutDimension.ThreeDimensional, size);
    }

    private static bool IsFiniteNumber(string field) =>
        double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value);
    private static LutValidationResult Invalid(string diagnostic) => new(false, Diagnostic: diagnostic);
}

internal sealed record FolderLutCandidate(string FilePath, string DisplayName, string FileName,
    string ContentSha256, int Size);

internal static class FolderLutScanner
{
    private const int MaxEntries = 100_000;

    public static (IReadOnlyList<FolderLutCandidate> Candidates, IReadOnlyList<LutFolderProblem> Problems) Scan(
        string folder, CancellationToken cancellationToken = default) => Scan(folder, false, cancellationToken);

    public static (IReadOnlyList<FolderLutCandidate> Candidates, IReadOnlyList<LutFolderProblem> Problems) Scan(
        string folder, bool includeSubfolders, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder)) return ([], string.IsNullOrWhiteSpace(folder) ? [] :
            [new("LUT folder", "The configured LUT folder does not exist or is unavailable.")]);
        var candidates = new List<FolderLutCandidate>();
        var problems = new List<LutFolderProblem>();
        var paths = new List<string>();
        var pending = new Stack<string>();
        pending.Push(folder);
        var entries = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var path in Directory.EnumerateFiles(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > MaxEntries)
                    {
                        problems.Add(new("LUT folder", $"Discovery stopped after {MaxEntries:N0} filesystem entries."));
                        pending.Clear();
                        break;
                    }
                    if (!Path.GetExtension(path).Equals(".cube", StringComparison.OrdinalIgnoreCase)) continue;
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    {
                        problems.Add(new(Path.GetRelativePath(folder, path), "Reparse-point file was skipped."));
                        continue;
                    }
                    paths.Add(path);
                }
                if (entries > MaxEntries) break;
                if (!includeSubfolders) continue;
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > MaxEntries)
                    {
                        problems.Add(new("LUT folder", $"Discovery stopped after {MaxEntries:N0} filesystem entries."));
                        pending.Clear();
                        break;
                    }
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        problems.Add(new(Path.GetRelativePath(folder, directory), "Reparse-point folder was skipped."));
                        continue;
                    }
                    pending.Push(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add(new(Path.GetRelativePath(folder, current), $"The folder could not be read: {exception.Message}"));
            }
        }
        foreach (var path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var content = File.ReadAllBytes(path);
                var validation = CubeLutValidator.Validate(content);
                if (!validation.IsValid)
                {
                    problems.Add(new(Path.GetFileName(path), validation.Diagnostic!));
                    continue;
                }
                candidates.Add(new(path, LutCatalog.MakeDisplayName(path), Path.GetFileName(path),
                    Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), validation.Size!.Value));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add(new(Path.GetFileName(path), $"The file could not be read: {exception.Message}"));
            }
        }
        return (candidates, problems);
    }
}

internal interface ILutLibrary
{
    Task<LutLibrarySnapshot> RefreshAsync(string folder, CancellationToken cancellationToken = default);
    Task<LutLibrarySnapshot> RefreshAsync(string folder, bool includeSubfolders,
        CancellationToken cancellationToken = default) => RefreshAsync(folder, cancellationToken);
}

internal sealed class CatalogFolderLutLibrary(Func<CatalogDatabaseSession?> session,
    Func<DateTimeOffset>? utcNow = null) : ILutLibrary
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public async Task<LutLibrarySnapshot> RefreshAsync(string folder, CancellationToken cancellationToken = default)
        => await RefreshAsync(folder, false, cancellationToken).ConfigureAwait(false);

    public async Task<LutLibrarySnapshot> RefreshAsync(string folder, bool includeSubfolders,
        CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<LutLibrarySnapshot>(() =>
            {
                var fullFolder = NormalizeFolder(folder);
                var scan = FolderLutScanner.Scan(fullFolder, includeSubfolders, cancellationToken);
                var distinct = scan.Candidates.GroupBy(candidate => candidate.ContentSha256, StringComparer.Ordinal)
                    .Select(group => group.OrderBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase).First())
                    .ToArray();
                using var connection = RequireSession().OpenConnection();
                using var transaction = connection.BeginTransaction();
                var resources = new List<ManagedLutResource>();
                foreach (var candidate in distinct)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resource = FindByHash(connection, transaction, candidate.ContentSha256);
                    var now = FormatUtc(_utcNow());
                    if (resource is null)
                    {
                        resource = new(StableId(candidate.ContentSha256), candidate.DisplayName, candidate.FileName,
                            candidate.ContentSha256, LutDimension.ThreeDimensional, candidate.Size,
                            LutResourceAvailability.Available, candidate.FilePath);
                        using var insert = connection.CreateCommand();
                        insert.Transaction = transaction;
                        insert.CommandText = """
                            INSERT INTO LutResources
                                (LutId,DisplayName,OriginalFileName,ContentSha256,LutKind,LutSize,CreatedUtc,UpdatedUtc)
                            VALUES ($id,$name,$file,$hash,'3d',$size,$now,$now);
                            """;
                        AddResourceParameters(insert, resource, now);
                        insert.ExecuteNonQuery();
                    }
                    else
                    {
                        resource = resource with { DisplayName = candidate.DisplayName, OriginalFileName = candidate.FileName,
                            FilePath = candidate.FilePath, Availability = LutResourceAvailability.Available, Diagnostic = null };
                        using var update = connection.CreateCommand();
                        update.Transaction = transaction;
                        update.CommandText = """
                            UPDATE LutResources SET DisplayName=$name,OriginalFileName=$file,UpdatedUtc=$now
                            WHERE LutId=$id;
                            """;
                        AddResourceParameters(update, resource, now);
                        update.ExecuteNonQuery();
                    }
                    resources.Add(resource);
                }
                transaction.Commit();
                return new(fullFolder, resources.OrderBy(resource => resource.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(resource => resource.FilePath, StringComparer.OrdinalIgnoreCase).ToArray(), scan.Problems);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _refreshGate.Release(); }
    }

    private static ManagedLutResource? FindByHash(SqliteConnection connection, SqliteTransaction transaction, string hash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT LutId,DisplayName,OriginalFileName,ContentSha256,LutSize FROM LutResources WHERE ContentSha256=$hash;";
        command.Parameters.AddWithValue("$hash", hash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadStored(reader) : null;
    }

    private static ManagedLutResource ReadStored(SqliteDataReader reader) =>
        new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            LutDimension.ThreeDimensional, reader.GetInt32(4), LutResourceAvailability.Missing);

    private static void AddResourceParameters(SqliteCommand command, ManagedLutResource resource, string now)
    {
        command.Parameters.AddWithValue("$id", resource.LutId.ToString("D"));
        command.Parameters.AddWithValue("$name", resource.DisplayName);
        command.Parameters.AddWithValue("$file", resource.OriginalFileName);
        command.Parameters.AddWithValue("$hash", resource.ContentSha256);
        command.Parameters.AddWithValue("$size", resource.Size);
        command.Parameters.AddWithValue("$now", now);
    }

    private static string NormalizeFolder(string folder) => string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder.Trim());
    private static Guid StableId(string contentSha256) => new(Convert.FromHexString(contentSha256)[..16]);
    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
    private static string FormatUtc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}

internal enum ColorLutStage { Camera, Creative }

internal interface ILutLibraryCache
{
    Task InitializeAsync(string cameraFolder, string creativeFolder, CancellationToken cancellationToken = default);
    Task InitializeAsync(string cameraFolder, bool cameraIncludeSubfolders,
        string creativeFolder, bool creativeIncludeSubfolders, CancellationToken cancellationToken = default) =>
        InitializeAsync(cameraFolder, creativeFolder, cancellationToken);
    Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder,
        CancellationToken cancellationToken = default);
    Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder, bool includeSubfolders,
        CancellationToken cancellationToken = default) => RefreshAsync(stage, folder, cancellationToken);
    LutLibrarySnapshot Snapshot(ColorLutStage stage);
    ManagedLutResource? Get(ColorLutStage stage, Guid lutId);
    string ResolvePath(ColorLutStage stage, Guid lutId);
    Task<CubeLutData> GetRuntimeAsync(ColorLutStage stage, Guid lutId,
        CancellationToken cancellationToken = default);
}

/// <summary>One authoritative runtime view of both configured LUT roots. Only startup and Settings-root
/// changes invoke the scanner; Player, Catalog Color availability, and Encoding are in-memory consumers.</summary>
internal sealed class ApplicationLutLibraryCache(ILutLibrary scanner, Func<string, CubeLutData>? runtimeLoader = null)
    : ILutLibraryCache, IDisposable
{
    private const int MaximumRuntimeEntries = 16;
    private const long MaximumRuntimeBytes = 256L * 1024 * 1024;

    private sealed class RuntimeEntry(Lazy<Task<CubeLutData>> value, long lastAccess)
    {
        public Lazy<Task<CubeLutData>> Value { get; } = value;
        public long LastAccess { get; set; } = lastAccess;
        public long LoadedBytes => Value.IsValueCreated && Value.Value.IsCompletedSuccessfully
            ? (long)Value.Value.Result.Samples.Length * sizeof(float) : 0;
    }

    private sealed class StageState
    {
        public LutLibrarySnapshot Snapshot { get; set; } = new("", [], []);
        public long Revision { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
    }

    private readonly object _gate = new();
    private readonly StageState _camera = new();
    private readonly StageState _creative = new();
    private readonly Dictionary<Guid, RuntimeEntry> _runtime = [];
    private readonly Func<string, CubeLutData> _runtimeLoader = runtimeLoader ?? CubeLutData.Load;
    private long _runtimeAccess;
    private Task? _initialization;

    public Task InitializeAsync(string cameraFolder, string creativeFolder,
        CancellationToken cancellationToken = default) =>
        InitializeAsync(cameraFolder, false, creativeFolder, false, cancellationToken);

    public Task InitializeAsync(string cameraFolder, bool cameraIncludeSubfolders,
        string creativeFolder, bool creativeIncludeSubfolders,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return _initialization ??= InitializeCoreAsync(cameraFolder, cameraIncludeSubfolders,
                creativeFolder, creativeIncludeSubfolders, cancellationToken);
    }

    private async Task InitializeCoreAsync(string cameraFolder, bool cameraIncludeSubfolders,
        string creativeFolder, bool creativeIncludeSubfolders, CancellationToken token)
    {
        await RefreshAsync(ColorLutStage.Camera, cameraFolder, cameraIncludeSubfolders, token).ConfigureAwait(false);
        await RefreshAsync(ColorLutStage.Creative, creativeFolder, creativeIncludeSubfolders, token).ConfigureAwait(false);
    }

    public Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder,
        CancellationToken cancellationToken = default) => RefreshAsync(stage, folder, false, cancellationToken);

    public async Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder, bool includeSubfolders,
        CancellationToken cancellationToken = default)
    {
        StageState state;
        long revision;
        CancellationTokenSource refreshCancellation;
        lock (_gate)
        {
            state = State(stage);
            revision = ++state.Revision;
            state.Cancellation?.Cancel();
            state.Cancellation?.Dispose();
            refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            state.Cancellation = refreshCancellation;
        }
        try
        {
            var snapshot = await scanner.RefreshAsync(folder, includeSubfolders, refreshCancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (state.Revision == revision)
                {
                    state.Snapshot = snapshot;
                    InvalidateRuntimeEntries();
                }
                return state.Snapshot;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_gate) return state.Snapshot;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(state.Cancellation, refreshCancellation)) state.Cancellation = null;
            }
            refreshCancellation.Dispose();
        }
    }

    public LutLibrarySnapshot Snapshot(ColorLutStage stage)
    {
        lock (_gate) return State(stage).Snapshot;
    }

    public ManagedLutResource? Get(ColorLutStage stage, Guid lutId) =>
        Snapshot(stage).Resources.FirstOrDefault(resource => resource.LutId == lutId);

    public string ResolvePath(ColorLutStage stage, Guid lutId)
    {
        var resource = Get(stage, lutId) ?? throw new FileNotFoundException(
            "The assigned LUT is not present in the configured LUT collection.");
        if (resource.Availability != LutResourceAvailability.Available || string.IsNullOrWhiteSpace(resource.FilePath))
            throw new FileNotFoundException(resource.Diagnostic ?? "The assigned LUT is unavailable.");
        return resource.FilePath;
    }

    public async Task<CubeLutData> GetRuntimeAsync(ColorLutStage stage, Guid lutId,
        CancellationToken cancellationToken = default)
    {
        RuntimeEntry entry;
        lock (_gate)
        {
            var path = ResolvePathLocked(stage, lutId);
            if (!_runtime.TryGetValue(lutId, out entry!))
            {
                entry = new(new(() => Task.Run(() => _runtimeLoader(path)),
                    LazyThreadSafetyMode.ExecutionAndPublication), ++_runtimeAccess);
                _runtime.Add(lutId, entry);
            }
            else entry.LastAccess = ++_runtimeAccess;
        }
        try { return await entry.Value.Value.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch
        {
            lock (_gate)
                if ((entry.Value.Value.IsFaulted || entry.Value.Value.IsCanceled)
                    && _runtime.TryGetValue(lutId, out var current) && ReferenceEquals(current, entry))
                    _runtime.Remove(lutId);
            throw;
        }
        finally
        {
            lock (_gate) TrimRuntimeCache(lutId);
        }
    }

    private string ResolvePathLocked(ColorLutStage stage, Guid lutId)
    {
        var resource = State(stage).Snapshot.Resources.FirstOrDefault(item => item.LutId == lutId)
            ?? throw new FileNotFoundException("The assigned LUT is not present in the configured LUT collection.");
        if (resource.Availability != LutResourceAvailability.Available || string.IsNullOrWhiteSpace(resource.FilePath))
            throw new FileNotFoundException(resource.Diagnostic ?? "The assigned LUT is unavailable.");
        return resource.FilePath;
    }

    private void InvalidateRuntimeEntries()
    {
        var validIds = _camera.Snapshot.Resources.Concat(_creative.Snapshot.Resources)
            .Where(resource => resource.Availability == LutResourceAvailability.Available)
            .Select(resource => resource.LutId).ToHashSet();
        foreach (var lutId in _runtime.Keys.Where(lutId => !validIds.Contains(lutId)).ToArray())
            _runtime.Remove(lutId);
    }

    private void TrimRuntimeCache(Guid retainedLutId)
    {
        while (_runtime.Count > MaximumRuntimeEntries || _runtime.Values.Sum(entry => entry.LoadedBytes) > MaximumRuntimeBytes)
        {
            var oldest = _runtime.Where(item => item.Key != retainedLutId && item.Value.Value.IsValueCreated
                                                && item.Value.Value.Value.IsCompleted)
                .OrderBy(item => item.Value.LastAccess).Select(item => (Guid?)item.Key).FirstOrDefault();
            if (oldest is null) return;
            _runtime.Remove(oldest.Value);
        }
    }

    private StageState State(ColorLutStage stage) => stage == ColorLutStage.Camera ? _camera : _creative;

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var state in new[] { _camera, _creative })
            {
                state.Cancellation?.Cancel();
                state.Cancellation?.Dispose();
                state.Cancellation = null;
            }
        }
    }
}

internal sealed record ColorLutReference(Guid LutId, string DisplayName, string ContentSha256,
    LutResourceAvailability Availability, string? Diagnostic = null);
internal sealed record AssetColorIntent(Guid AssetId, ColorLutReference? Camera, ColorLutReference? Creative,
    string ColorIdentity, bool ColorEnabled = false)
{
    public IReadOnlyList<ColorLutReference> OrderedPipeline => new[] { Camera, Creative }.OfType<ColorLutReference>().ToArray();
    public bool HasColor => Camera is not null || Creative is not null;
}
internal sealed record ColorAssignmentChange(Guid AssetId, Guid? CameraLutId, Guid? CreativeLutId);

internal interface IAssetColorStore
{
    Task<AssetColorIntent> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, AssetColorIntent>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    Task SetStageAsync(IReadOnlyCollection<Guid> assetIds, ColorLutStage stage, Guid? lutId,
        CancellationToken cancellationToken = default);
    Task SetColorEnabledAsync(IReadOnlyCollection<Guid> assetIds, bool enabled,
        CancellationToken cancellationToken = default);
    Task SetAsync(IReadOnlyCollection<ColorAssignmentChange> changes, CancellationToken cancellationToken = default);
}

internal sealed class CatalogAssetColorStore(Func<CatalogDatabaseSession?> session, ILutLibraryCache lutCache,
    Func<DateTimeOffset>? utcNow = null) : IAssetColorStore
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<AssetColorIntent> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        (await GetAsync([assetId], cancellationToken).ConfigureAwait(false))[assetId];

    public Task<IReadOnlyDictionary<Guid, AssetColorIntent>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default) => Task.Run<IReadOnlyDictionary<Guid, AssetColorIntent>>(() =>
    {
        var ids = assetIds.Distinct().ToArray();
        var result = ids.ToDictionary(id => id, Empty);
        if (ids.Length == 0) return result;
        var availableCamera = lutCache.Snapshot(ColorLutStage.Camera).Resources.ToDictionary(
            resource => resource.ContentSha256, StringComparer.Ordinal);
        var availableCreative = lutCache.Snapshot(ColorLutStage.Creative).Resources.ToDictionary(
            resource => resource.ContentSha256, StringComparer.Ordinal);
        using var connection = RequireSession().OpenConnection();
        foreach (var batch in ids.Chunk(400))
        {
            using var command = connection.CreateCommand();
            var parameters = batch.Select((id, index) =>
            { var name = $"$id{index}"; command.Parameters.AddWithValue(name, id.ToString("D")); return name; }).ToArray();
            command.CommandText = $"""
                SELECT c.AssetId,c.CameraLutId,cam.DisplayName,cam.ContentSha256,
                       c.CreativeLutId,creative.DisplayName,creative.ContentSha256,c.ColorEnabled
                FROM MediaAssetColor c
                LEFT JOIN LutResources cam ON cam.LutId=c.CameraLutId
                LEFT JOIN LutResources creative ON creative.LutId=c.CreativeLutId
                WHERE c.AssetId IN ({string.Join(',', parameters)});
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = Guid.Parse(reader.GetString(0));
                var camera = ReadReference(reader, 1, availableCamera);
                var creative = ReadReference(reader, 4, availableCreative);
                var enabled = reader.GetInt64(7) != 0;
                result[assetId] = new(assetId, camera, creative, Identity(enabled, camera, creative), enabled);
            }
        }
        return result;
    }, cancellationToken);

    public Task SetStageAsync(IReadOnlyCollection<Guid> assetIds, ColorLutStage stage, Guid? lutId,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var ids = assetIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var assetId in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureExists(connection, transaction, "MediaAssets", "AssetId", assetId, "Catalog asset");
        }
        if (lutId is Guid resourceId)
            EnsureExists(connection, transaction, "LutResources", "LutId", resourceId,
                stage == ColorLutStage.Camera ? "Camera LUT" : "Creative LUT");
        var now = FormatUtc(_utcNow());
        foreach (var assetId in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = lutId is null
                ? (stage == ColorLutStage.Camera
                    ? "DELETE FROM MediaAssetColor WHERE AssetId=$asset AND CreativeLutId IS NULL AND ColorEnabled=0; UPDATE MediaAssetColor SET CameraLutId=NULL,UpdatedUtc=$now WHERE AssetId=$asset;"
                    : "DELETE FROM MediaAssetColor WHERE AssetId=$asset AND CameraLutId IS NULL AND ColorEnabled=0; UPDATE MediaAssetColor SET CreativeLutId=NULL,UpdatedUtc=$now WHERE AssetId=$asset;")
                : stage == ColorLutStage.Camera ? """
                    INSERT INTO MediaAssetColor (AssetId,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc)
                    VALUES ($asset,$lut,NULL,$now,$now)
                    ON CONFLICT(AssetId) DO UPDATE SET CameraLutId=excluded.CameraLutId,UpdatedUtc=excluded.UpdatedUtc;
                    """ : """
                    INSERT INTO MediaAssetColor (AssetId,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc)
                    VALUES ($asset,NULL,$lut,$now,$now)
                    ON CONFLICT(AssetId) DO UPDATE SET CreativeLutId=excluded.CreativeLutId,UpdatedUtc=excluded.UpdatedUtc;
                    """;
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            command.Parameters.AddWithValue("$lut", lutId?.ToString("D") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }, cancellationToken);

    public Task SetColorEnabledAsync(IReadOnlyCollection<Guid> assetIds, bool enabled,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var ids = assetIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = FormatUtc(_utcNow());
        foreach (var assetId in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureExists(connection, transaction, "MediaAssets", "AssetId", assetId, "Catalog asset");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO MediaAssetColor (AssetId,ColorEnabled,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc)
                VALUES ($asset,$enabled,NULL,NULL,$now,$now)
                ON CONFLICT(AssetId) DO UPDATE SET ColorEnabled=excluded.ColorEnabled,UpdatedUtc=excluded.UpdatedUtc;
                """;
            command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }, cancellationToken);

    public Task SetAsync(IReadOnlyCollection<ColorAssignmentChange> changes,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var normalized = changes.GroupBy(change => change.AssetId).Select(group => group.Last()).ToArray();
        if (normalized.Length == 0) return;
        using var connection = RequireSession().OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var change in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureExists(connection, transaction, "MediaAssets", "AssetId", change.AssetId, "Catalog asset");
            if (change.CameraLutId is Guid camera) EnsureExists(connection, transaction, "LutResources", "LutId", camera, "Camera LUT");
            if (change.CreativeLutId is Guid creative) EnsureExists(connection, transaction, "LutResources", "LutId", creative, "Creative LUT");
        }
        var now = FormatUtc(_utcNow());
        foreach (var change in normalized)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$asset", change.AssetId.ToString("D"));
            if (change.CameraLutId is null && change.CreativeLutId is null)
                command.CommandText = "DELETE FROM MediaAssetColor WHERE AssetId=$asset AND ColorEnabled=0; UPDATE MediaAssetColor SET CameraLutId=NULL,CreativeLutId=NULL,UpdatedUtc=$now WHERE AssetId=$asset;";
            else
            {
                command.CommandText = """
                    INSERT INTO MediaAssetColor (AssetId,CameraLutId,CreativeLutId,CreatedUtc,UpdatedUtc)
                    VALUES ($asset,$camera,$creative,$now,$now)
                    ON CONFLICT(AssetId) DO UPDATE SET CameraLutId=excluded.CameraLutId,
                        CreativeLutId=excluded.CreativeLutId,UpdatedUtc=excluded.UpdatedUtc;
                    """;
                command.Parameters.AddWithValue("$camera", change.CameraLutId?.ToString("D") ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$creative", change.CreativeLutId?.ToString("D") ?? (object)DBNull.Value);
            }
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }, cancellationToken);

    private static void EnsureExists(SqliteConnection connection, SqliteTransaction transaction, string table,
        string column, Guid id, string label)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT count(*) FROM {table} WHERE {column}=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        if (Convert.ToInt64(command.ExecuteScalar()) != 1) throw new InvalidOperationException($"{label} '{id:D}' does not exist.");
    }

    private static ColorLutReference? ReadReference(SqliteDataReader reader, int offset,
        IReadOnlyDictionary<string, ManagedLutResource> available)
    {
        if (reader.IsDBNull(offset)) return null;
        var id = Guid.Parse(reader.GetString(offset));
        if (reader.IsDBNull(offset + 1) || reader.IsDBNull(offset + 2))
            return new(id, "Unavailable LUT", "", LutResourceAvailability.Missing, "The assigned LUT resource is unavailable.");
        var name = reader.GetString(offset + 1);
        var hash = reader.GetString(offset + 2);
        return available.TryGetValue(hash, out var current)
            ? new(id, current.DisplayName, hash, LutResourceAvailability.Available)
            : new(id, name, hash, LutResourceAvailability.Missing,
                "The assigned LUT is not present in the configured LUT folder.");
    }

    private static AssetColorIntent Empty(Guid assetId) => new(assetId, null, null, Identity(false, null, null));
    private static string Identity(bool enabled, ColorLutReference? camera, ColorLutReference? creative)
    {
        var contract = $"lightflow-color-v2\nenabled:{enabled}\ncamera:{camera?.LutId:D}:{camera?.ContentSha256}\ncreative:{creative?.LutId:D}:{creative?.ContentSha256}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract))).ToLowerInvariant();
    }
    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
    private static string FormatUtc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
