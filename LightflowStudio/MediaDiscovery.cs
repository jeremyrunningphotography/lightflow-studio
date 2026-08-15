using System.IO;

namespace LightflowStudio;

internal enum MediaTypeCategory
{
    StillImage,
    RawImage,
    Video,
    Audio,
    Unknown
}

internal sealed record MediaTypeClassification(MediaTypeCategory Category, string? FormatKey = null)
{
    public bool IsKnown => Category != MediaTypeCategory.Unknown;
    public static MediaTypeClassification Unknown { get; } = new(MediaTypeCategory.Unknown);
}

internal sealed record MediaTypeClassificationContext(
    string FileName,
    string? DeclaredContentType = null,
    ReadOnlyMemory<byte> Header = default);

internal interface IMediaTypeClassifier
{
    bool TryClassify(MediaTypeClassificationContext context, out MediaTypeClassification classification);
}

internal interface IMediaTypeRegistry
{
    MediaTypeClassification Classify(MediaTypeClassificationContext context);
}

internal sealed class MediaTypeRegistry(IEnumerable<IMediaTypeClassifier> classifiers) : IMediaTypeRegistry
{
    private readonly IReadOnlyList<IMediaTypeClassifier> _classifiers = classifiers.ToArray();

    public static MediaTypeRegistry CreateDefault() => new(
    [
        new ExtensionMediaTypeClassifier(MediaTypeCategory.StillImage,
            ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp", ".heic", ".heif",
            ".avif", ".wdp", ".jxr"),
        new ExtensionMediaTypeClassifier(MediaTypeCategory.RawImage,
            ".3fr", ".arw", ".cr2", ".cr3", ".dng", ".erf", ".iiq", ".kdc", ".mef", ".mos",
            ".mrw", ".nef", ".nrw", ".orf", ".pef", ".raf", ".raw", ".rw2", ".rwl", ".sr2",
            ".srf", ".srw", ".x3f"),
        new ExtensionMediaTypeClassifier(MediaTypeCategory.Video,
            ".3gp", ".avi", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts",
            ".mxf", ".ts", ".webm", ".wmv"),
        new ExtensionMediaTypeClassifier(MediaTypeCategory.Audio,
            ".aac", ".aif", ".aiff", ".alac", ".flac", ".m4a", ".mp3", ".ogg", ".opus", ".wav",
            ".wma")
    ]);

    public MediaTypeClassification Classify(MediaTypeClassificationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.FileName);
        foreach (var classifier in _classifiers)
            if (classifier.TryClassify(context, out var classification)) return classification;
        return MediaTypeClassification.Unknown;
    }
}

internal sealed class ExtensionMediaTypeClassifier : IMediaTypeClassifier
{
    private readonly MediaTypeCategory _category;
    private readonly HashSet<string> _extensions;

    public ExtensionMediaTypeClassifier(MediaTypeCategory category, params string[] extensions)
    {
        if (category == MediaTypeCategory.Unknown)
            throw new ArgumentException("An extension classifier must classify a known media type.", nameof(category));
        _category = category;
        _extensions = extensions.Select(NormalizeExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool TryClassify(MediaTypeClassificationContext context, out MediaTypeClassification classification)
    {
        var extension = Path.GetExtension(context.FileName);
        if (!string.IsNullOrWhiteSpace(extension) && _extensions.Contains(extension))
        {
            classification = new(_category, extension.TrimStart('.').ToLowerInvariant());
            return true;
        }
        classification = MediaTypeClassification.Unknown;
        return false;
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }
}

internal sealed record MediaFolderFileSystemEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long? FileSizeBytes,
    DateTimeOffset LastWriteUtc);

internal interface IMediaFolderFileSystem
{
    Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(
        string folderPath,
        CancellationToken cancellationToken = default);
}

internal sealed class MediaFolderFileSystem : IMediaFolderFileSystem
{
    public Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(
        string folderPath,
        CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<MediaFolderFileSystemEntry>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = new DirectoryInfo(folderPath);
        var entries = new List<MediaFolderFileSystemEntry>();
        foreach (var item in directory.EnumerateFileSystemInfos("*", new EnumerationOptions
        {
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0
        }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = item is DirectoryInfo;
            entries.Add(new(item.FullName, item.Name, isDirectory,
                item is FileInfo file ? file.Length : null, item.LastWriteTimeUtc));
        }
        return entries;
    }, cancellationToken);
}

internal sealed record MediaFolderEnumerationRequest(Guid RootId, string? RelativeFolder = null);

internal sealed record MediaFolderEntry(
    Guid RootId,
    string RelativePath,
    string RelativePathKey,
    string Name,
    bool IsDirectory,
    MediaTypeClassification MediaType,
    long? FileSizeBytes,
    DateTimeOffset LastWriteUtc);

internal enum MediaFolderEnumerationStatus
{
    Succeeded,
    RootNotFound,
    RootUnavailable,
    FolderNotFound,
    FolderUnavailable,
    AccessDenied,
    InvalidPath,
    Failed
}

internal sealed record MediaFolderEnumerationResult(
    MediaFolderEnumerationStatus Status,
    string RelativeFolder,
    IReadOnlyList<MediaFolderEntry> Entries,
    string? Diagnostic = null)
{
    public bool Succeeded => Status == MediaFolderEnumerationStatus.Succeeded;
}

internal interface IMediaFolderEnumerator
{
    Task<MediaFolderEnumerationResult> EnumerateAsync(
        MediaFolderEnumerationRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class MediaFolderEnumerator(
    IMediaRootService roots,
    IMediaTypeRegistry mediaTypes,
    IMediaFolderFileSystem fileSystem,
    int maximumConcurrency = 2) : IMediaFolderEnumerator
{
    private readonly SemaphoreSlim _concurrency = new(maximumConcurrency > 0
        ? maximumConcurrency
        : throw new ArgumentOutOfRangeException(nameof(maximumConcurrency)), maximumConcurrency);

    public async Task<MediaFolderEnumerationResult> EnumerateAsync(
        MediaFolderEnumerationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = await roots.GetAsync(request.RootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
            return Result(MediaFolderEnumerationStatus.RootNotFound, "", "The Media Root does not exist.");
        if (root.Availability != MediaRootAvailability.Online || string.IsNullOrWhiteSpace(root.PhysicalPath))
            return Result(MediaFolderEnumerationStatus.RootUnavailable, "", root.Diagnostic ??
                "The Media Root is not available on this computer.");

        string relativeFolder;
        string physicalFolder;
        try
        {
            relativeFolder = NormalizeFolder(request.RelativeFolder);
            physicalFolder = relativeFolder.Length == 0
                ? MediaPathSemantics.NormalizeRootPath(root.PhysicalPath)
                : MediaPathSemantics.ResolveContained(root.PhysicalPath, relativeFolder);
        }
        catch (ArgumentException exception)
        {
            return Result(MediaFolderEnumerationStatus.InvalidPath, "", exception.Message);
        }

        IReadOnlyList<MediaFolderFileSystemEntry> sourceEntries;
        try
        {
            sourceEntries = await EnumerateSourceAsync(physicalFolder, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException exception)
        {
            return Result(MediaFolderEnumerationStatus.AccessDenied, relativeFolder,
                $"Lightflow cannot access this folder: {exception.Message}");
        }
        catch (System.Security.SecurityException exception)
        {
            return Result(MediaFolderEnumerationStatus.AccessDenied, relativeFolder,
                $"Lightflow cannot access this folder: {exception.Message}");
        }
        catch (DirectoryNotFoundException exception)
        {
            return Result(MediaFolderEnumerationStatus.FolderNotFound, relativeFolder, exception.Message);
        }
        catch (IOException exception)
        {
            return Result(MediaFolderEnumerationStatus.FolderUnavailable, relativeFolder,
                $"The folder is temporarily unavailable: {exception.Message}");
        }

        var entries = new List<MediaFolderEntry>(sourceEntries.Count);
        try
        {
            foreach (var source in sourceEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(source.FullPath);
                var relative = MediaPathSemantics.NormalizeRelativePath(
                    Path.GetRelativePath(root.PhysicalPath, fullPath));
                var contained = MediaPathSemantics.ResolveContained(root.PhysicalPath, relative);
                if (!string.Equals(fullPath, contained, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The filesystem returned an entry outside the Media Root.");
                var type = source.IsDirectory
                    ? MediaTypeClassification.Unknown
                    : mediaTypes.Classify(new(source.Name));
                entries.Add(new(root.RootId, relative, MediaPathSemantics.RelativePathKey(relative), source.Name,
                    source.IsDirectory, type, source.FileSizeBytes, source.LastWriteUtc));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return Result(MediaFolderEnumerationStatus.Failed, relativeFolder,
                $"The folder returned an invalid entry: {exception.Message}");
        }

        entries.Sort(MediaFolderEntryComparer.Instance);
        return new(MediaFolderEnumerationStatus.Succeeded, relativeFolder, entries);
    }

    private async Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateSourceAsync(
        string physicalFolder,
        CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await fileSystem.EnumerateAsync(physicalFolder, cancellationToken).ConfigureAwait(false); }
        finally { _concurrency.Release(); }
    }

    private static string NormalizeFolder(string? relativeFolder)
    {
        if (string.IsNullOrWhiteSpace(relativeFolder) || relativeFolder.Trim() == ".") return "";
        return MediaPathSemantics.NormalizeRelativePath(relativeFolder);
    }

    private static MediaFolderEnumerationResult Result(
        MediaFolderEnumerationStatus status,
        string relativeFolder,
        string diagnostic) => new(status, relativeFolder, [], diagnostic);

    private sealed class MediaFolderEntryComparer : IComparer<MediaFolderEntry>
    {
        public static MediaFolderEntryComparer Instance { get; } = new();

        public int Compare(MediaFolderEntry? left, MediaFolderEntry? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var directoryOrder = right.IsDirectory.CompareTo(left.IsDirectory);
            if (directoryOrder != 0) return directoryOrder;
            var nameOrder = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameOrder != 0 ? nameOrder : StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
        }
    }
}
