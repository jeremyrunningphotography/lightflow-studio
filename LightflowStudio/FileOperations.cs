using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace LightflowStudio;

internal enum FileOperationKind { Copy, Move, Recycle, PermanentDelete }
internal enum FileOperationExecution { Direct, Job }
internal enum FileOperationState { Waiting, Running, Completed, CompletedWithFailures, Failed, Cancelled, Interrupted }

internal sealed record FileOperationSource(Guid? AssetId, string Path, long? SizeBytes = null, bool IsDirectory = false);
internal sealed record FileOperationIntent(Guid OperationId, FileOperationKind Kind,
    IReadOnlyList<FileOperationSource> Sources, string? Destination, DateTimeOffset CreatedUtc,
    long? EstimatedBytes, bool CrossVolume, FileOperationExecution Execution);
internal sealed record FileOperationFailure(string Path, string Diagnostic);
internal sealed record FileOperationResult(Guid OperationId, FileOperationState State, int CompletedItems,
    long CompletedBytes, IReadOnlyList<FileOperationFailure> Failures, DateTimeOffset CompletedUtc)
{
    public bool Succeeded => State == FileOperationState.Completed;
}

/// <summary>One centralized, deterministic direct-versus-Job policy for all Browser file operations.</summary>
internal static class FileOperationPromotionPolicy
{
    public const int MaximumDirectItems = 8;
    public const long MaximumDirectBytes = 256L * 1024 * 1024;

    public static FileOperationExecution Decide(FileOperationKind kind, int itemCount, long? bytes,
        bool crossVolume, bool includesDirectory)
    {
        if (kind is FileOperationKind.Recycle or FileOperationKind.PermanentDelete)
            return itemCount <= MaximumDirectItems && !includesDirectory
                ? FileOperationExecution.Direct : FileOperationExecution.Job;
        if (crossVolume || includesDirectory || itemCount > MaximumDirectItems || bytes is null || bytes > MaximumDirectBytes)
            return FileOperationExecution.Job;
        return FileOperationExecution.Direct;
    }
}

internal static class FileOperationPathSemantics
{
    public static bool SameVolume(string left, string right)
    {
        var leftRoot = Path.GetPathRoot(Path.GetFullPath(left));
        var rightRoot = Path.GetPathRoot(Path.GetFullPath(right));
        return !string.IsNullOrWhiteSpace(leftRoot) && !string.IsNullOrWhiteSpace(rightRoot) &&
            string.Equals(Path.TrimEndingDirectorySeparator(leftRoot), Path.TrimEndingDirectorySeparator(rightRoot),
                StringComparison.OrdinalIgnoreCase);
    }

    public static FileOperationKind DragKind(string source, string destination, bool control, bool shift)
    {
        if (control && !shift) return FileOperationKind.Copy;
        if (shift && !control) return FileOperationKind.Move;
        return SameVolume(source, destination) ? FileOperationKind.Move : FileOperationKind.Copy;
    }

    public static bool IsSameOrDescendant(string candidate, string ancestor)
    {
        var child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ancestor));
        return string.Equals(child, parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(
            parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class FileOperationPlanner
{
    public static FileOperationIntent Plan(FileOperationKind kind, IEnumerable<FileOperationSource> sources,
        string? destination, bool forceJob = false)
    {
        var materialized = sources.Select(source => source with { Path = Path.GetFullPath(source.Path) }).ToArray();
        if (materialized.Length == 0) throw new ArgumentException("Select at least one filesystem item.");
        if (materialized.Select(source => source.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materialized.Length)
            throw new ArgumentException("The selection contains the same filesystem item more than once.");
        string? target = null;
        if (kind is FileOperationKind.Copy or FileOperationKind.Move)
        {
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Choose a destination folder.");
            target = Path.GetFullPath(destination);
            if (!Directory.Exists(target)) throw new DirectoryNotFoundException("The destination folder is unavailable.");
            foreach (var source in materialized)
            {
                if (!File.Exists(source.Path) && !Directory.Exists(source.Path))
                    throw new FileNotFoundException("A selected source is unavailable.", source.Path);
                if (string.Equals(source.Path, Path.Combine(target, Path.GetFileName(source.Path)), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("A source and destination are the same filesystem item.");
                if (source.IsDirectory && FileOperationPathSemantics.IsSameOrDescendant(target, source.Path))
                    throw new IOException("A folder cannot be copied or moved into itself or one of its descendants.");
                var proposed = Path.Combine(target, Path.GetFileName(source.Path));
                if (File.Exists(proposed) || Directory.Exists(proposed))
                    throw new IOException($"The destination already contains ‘{Path.GetFileName(source.Path)}’. Nothing was overwritten.");
            }
        }
        var bytesKnown = materialized.All(source => source.SizeBytes is not null);
        long? bytes = bytesKnown ? materialized.Sum(source => source.SizeBytes!.Value) : null;
        var cross = target is not null && materialized.Any(source => !FileOperationPathSemantics.SameVolume(source.Path, target));
        var execution = forceJob ? FileOperationExecution.Job : FileOperationPromotionPolicy.Decide(kind,
            materialized.Length, bytes, cross, materialized.Any(source => source.IsDirectory));
        return new(Guid.NewGuid(), kind, materialized, target, DateTimeOffset.UtcNow, bytes, cross, execution);
    }
}

internal interface IFileOperationPlatform
{
    Task CopyFileAsync(string source, string destination, IProgress<long>? progress, CancellationToken cancellationToken);
    void Move(string source, string destination);
    void Recycle(string path);
    void PermanentlyDelete(string path);
}

internal sealed class WindowsFileOperationPlatform : IFileOperationPlatform
{
    public async Task CopyFileAsync(string source, string destination, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                progress?.Report(read);
            }
        }
        catch { try { File.Delete(destination); } catch { } throw; }
    }

    public void Move(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination);
    }

    public void Recycle(string path)
    {
        if (Directory.Exists(path)) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else if (File.Exists(path)) FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else throw new FileNotFoundException("The selected item is unavailable.", path);
    }

    public void PermanentlyDelete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else if (File.Exists(path)) File.Delete(path);
        else throw new FileNotFoundException("The selected item is unavailable.", path);
    }
}

internal sealed class FileOperationExecutor(IFileOperationPlatform platform, IMediaAssetService assets,
    IBrowserLocationResolver locations)
{
    public async Task<FileOperationResult> ExecuteAsync(FileOperationIntent intent,
        IProgress<(int Items, long Bytes, string Current)>? progress = null, CancellationToken cancellationToken = default)
    {
        var failures = new List<FileOperationFailure>();
        var completedItems = 0;
        long completedBytes = 0;
        foreach (var source in intent.Sources)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var destination = intent.Destination is null ? null : Path.Combine(intent.Destination, Path.GetFileName(source.Path));
                switch (intent.Kind)
                {
                    case FileOperationKind.Copy:
                        if (source.IsDirectory) await CopyDirectoryAsync(source.Path, destination!, value =>
                        { completedBytes += value; progress?.Report((completedItems, completedBytes, source.Path)); }, cancellationToken);
                        else await platform.CopyFileAsync(source.Path, destination!, new Progress<long>(value =>
                        { completedBytes += value; progress?.Report((completedItems, completedBytes, source.Path)); }), cancellationToken);
                        await ReconcileCopyAsync(destination!, cancellationToken);
                        break;
                    case FileOperationKind.Move:
                        var catalogMoves = await CaptureCatalogMovesAsync(source, destination!, cancellationToken);
                        if (FileOperationPathSemantics.SameVolume(source.Path, destination!)) platform.Move(source.Path, destination!);
                        else
                        {
                            if (source.IsDirectory) await CopyDirectoryAsync(source.Path, destination!, value =>
                            { completedBytes += value; progress?.Report((completedItems, completedBytes, source.Path)); }, cancellationToken);
                            else await platform.CopyFileAsync(source.Path, destination!, new Progress<long>(value =>
                            { completedBytes += value; progress?.Report((completedItems, completedBytes, source.Path)); }), cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested(); // source remains intact until the copy is complete.
                            platform.PermanentlyDelete(source.Path);
                        }
                        foreach (var move in catalogMoves)
                        {
                            var relocation = await assets.RelocateAsync(move.AssetId, move.RootId, move.RelativePath, cancellationToken).ConfigureAwait(false);
                            if (relocation.Status != MediaAssetOperationStatus.Succeeded) throw new IOException(relocation.Diagnostic);
                        }
                        if (FileOperationPathSemantics.SameVolume(source.Path, destination!))
                            completedBytes += source.SizeBytes ?? 0;
                        break;
                    case FileOperationKind.Recycle:
                        platform.Recycle(source.Path);
                        if (source.AssetId is { } recycledId) await assets.MarkMissingAsync([recycledId], cancellationToken).ConfigureAwait(false);
                        break;
                    case FileOperationKind.PermanentDelete:
                        platform.PermanentlyDelete(source.Path);
                        if (source.AssetId is { } deletedId) await assets.MarkMissingAsync([deletedId], cancellationToken).ConfigureAwait(false);
                        break;
                }
                completedItems++;
                progress?.Report((completedItems, completedBytes, source.Path));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { failures.Add(new(source.Path, exception.Message)); }
        }
        var state = cancellationToken.IsCancellationRequested ? FileOperationState.Cancelled : failures.Count == 0
            ? FileOperationState.Completed : completedItems > 0 ? FileOperationState.CompletedWithFailures : FileOperationState.Failed;
        return new(intent.OperationId, state, completedItems, completedBytes, failures, DateTimeOffset.UtcNow);
    }

    private async Task ReconcileCopyAsync(string destination, CancellationToken cancellationToken)
    {
        var resolved = await locations.ResolveAsync(Path.GetDirectoryName(destination)!, cancellationToken).ConfigureAwait(false);
        if (!resolved.Succeeded) throw new IOException(resolved.Diagnostic);
        var relative = string.IsNullOrEmpty(resolved.RelativeFolder) ? Path.GetFileName(destination) :
            $"{resolved.RelativeFolder}/{Path.GetFileName(destination)}";
        await assets.CreateAsync(resolved.RootId!.Value, relative, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<(Guid AssetId, Guid RootId, string RelativePath)>> CaptureCatalogMovesAsync(
        FileOperationSource source, string destination, CancellationToken cancellationToken)
    {
        var targetFolder = Path.GetDirectoryName(destination)!;
        var target = await locations.ResolveAsync(targetFolder, cancellationToken).ConfigureAwait(false);
        if (!target.Succeeded) throw new IOException(target.Diagnostic);
        var moves = new List<(Guid, Guid, string)>();
        if (source.AssetId is { } one)
        {
            moves.Add((one, target.RootId!.Value, Combine(target.RelativeFolder, Path.GetFileName(destination))));
            return moves;
        }
        if (!source.IsDirectory) return moves;
        foreach (var asset in await assets.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var resolved = await assets.GetAsync(asset.AssetId, cancellationToken).ConfigureAwait(false);
            if (resolved?.PhysicalPath is not { } path || !FileOperationPathSemantics.IsSameOrDescendant(path, source.Path)) continue;
            var movedPath = Path.Combine(destination, Path.GetRelativePath(source.Path, path));
            moves.Add((asset.AssetId, target.RootId!.Value, Combine(target.RelativeFolder,
                Path.GetRelativePath(targetFolder, movedPath))));
        }
        return moves;
    }

    private static string Combine(string folder, string path) => MediaPathSemantics.NormalizeRelativePath(
        string.IsNullOrEmpty(folder) ? path : $"{folder}/{path}");

    private async Task CopyDirectoryAsync(string source, string destination, Action<long> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", System.IO.SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            await platform.CopyFileAsync(file, target, new Progress<long>(progress), cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed record FileOperationJobSnapshot(FileOperationIntent Intent, FileOperationState State,
    int CompletedItems, long CompletedBytes, string? CurrentItem, IReadOnlyList<FileOperationFailure> Failures,
    FileOperationResult? Result = null);

/// <summary>Bounded non-Export capability runtime projected into the shared Jobs surfaces.</summary>
internal sealed class FileOperationJobs
{
    private readonly FileOperationExecutor _executor;
    private readonly FileOperationHistoryStore _history;
    private readonly object _sync = new();
    private readonly List<FileOperationJobSnapshot> _jobs = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _cancellations = [];
    public FileOperationJobs(FileOperationExecutor executor, FileOperationHistoryStore history)
    { _executor = executor; _history = history; _history.RecoverInterrupted(); }
    public event Action? Changed;
    public IReadOnlyList<FileOperationJobSnapshot> Jobs { get { lock (_sync) return _jobs.ToArray(); } }
    public IReadOnlyList<FileOperationHistoryRecord> History => _history.Load();
    public void Enqueue(FileOperationIntent intent)
    {
        _history.Begin(intent);
        lock (_sync) _jobs.Add(new(intent, FileOperationState.Waiting, 0, 0, null, []));
        Changed?.Invoke();
        _ = RunAsync(intent);
    }
    public void Cancel(Guid id) { lock (_sync) if (_cancellations.TryGetValue(id, out var cts)) cts.Cancel(); }
    private async Task RunAsync(FileOperationIntent intent)
    {
        var cts = new CancellationTokenSource();
        lock (_sync) { _cancellations[intent.OperationId] = cts; Update(intent.OperationId, job => job with { State = FileOperationState.Running }); }
        Changed?.Invoke();
        var progress = new Progress<(int Items, long Bytes, string Current)>(value =>
        { lock (_sync) Update(intent.OperationId, job => job with { CompletedItems = value.Items, CompletedBytes = value.Bytes, CurrentItem = value.Current }); Changed?.Invoke(); });
        var result = await _executor.ExecuteAsync(intent, progress, cts.Token).ConfigureAwait(false);
        lock (_sync)
        {
            _cancellations.Remove(intent.OperationId);
            Update(intent.OperationId, job => job with { State = result.State, CompletedItems = result.CompletedItems,
                CompletedBytes = result.CompletedBytes, Failures = result.Failures, Result = result });
        }
        _history.Complete(intent, result);
        Changed?.Invoke();
        cts.Dispose();
    }
    private void Update(Guid id, Func<FileOperationJobSnapshot, FileOperationJobSnapshot> update)
    { var index = _jobs.FindIndex(job => job.Intent.OperationId == id); if (index >= 0) _jobs[index] = update(_jobs[index]); }
}

internal sealed record FileOperationHistoryRecord(FileOperationIntent Intent, FileOperationResult Result);
internal sealed class FileOperationHistoryStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _sync = new();
    public IReadOnlyList<FileOperationHistoryRecord> Load()
    {
        lock (_sync)
        {
            if (!File.Exists(path)) return [];
            try { return JsonSerializer.Deserialize<List<FileOperationHistoryRecord>>(File.ReadAllText(path), Json) ?? []; }
            catch (JsonException) { return []; }
        }
    }
    private string ActivePath => path + ".active";
    public void Begin(FileOperationIntent intent)
    {
        lock (_sync)
        {
            var active = LoadDocument<FileOperationIntent>(ActivePath).ToList();
            active.Add(intent); SaveDocument(ActivePath, active);
        }
    }
    public void Complete(FileOperationIntent intent, FileOperationResult result)
    {
        lock (_sync)
        {
            var records = Load().ToList(); records.Add(new(intent, result));
            SaveDocument(path, records);
            SaveDocument(ActivePath, LoadDocument<FileOperationIntent>(ActivePath)
                .Where(item => item.OperationId != intent.OperationId).ToList());
        }
    }
    public void RecoverInterrupted()
    {
        lock (_sync)
        {
            var active = LoadDocument<FileOperationIntent>(ActivePath);
            if (active.Count == 0) return;
            var records = Load().ToList();
            records.AddRange(active.Select(intent => new FileOperationHistoryRecord(intent,
                new(intent.OperationId, FileOperationState.Interrupted, 0, 0,
                    [new("", "Lightflow closed before this operation reported a terminal result; it was not resumed.")], DateTimeOffset.UtcNow))));
            SaveDocument(path, records); SaveDocument(ActivePath, Array.Empty<FileOperationIntent>());
        }
    }
    private static IReadOnlyList<T> LoadDocument<T>(string documentPath)
    {
        if (!File.Exists(documentPath)) return [];
        try { return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(documentPath), Json) ?? []; }
        catch (JsonException) { return []; }
    }
    private static void SaveDocument<T>(string documentPath, IReadOnlyCollection<T> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
        var temporary = documentPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(values, Json));
        File.Move(temporary, documentPath, true);
    }
}
