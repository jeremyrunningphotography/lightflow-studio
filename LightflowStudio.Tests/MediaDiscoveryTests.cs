using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class MediaTypeRegistryTests
{
    private readonly IMediaTypeRegistry _registry = MediaTypeRegistry.CreateDefault();

    [Theory]
    [InlineData("photo.JPG", "StillImage")]
    [InlineData("photo.heic", "StillImage")]
    [InlineData("negative.CR3", "RawImage")]
    [InlineData("negative.nef", "RawImage")]
    [InlineData("clip.MXF", "Video")]
    [InlineData("sound.flac", "Audio")]
    [InlineData("notes.txt", "Unknown")]
    [InlineData("extensionless", "Unknown")]
    public void DefaultRegistryClassifiesKnownAndUnknownMedia(string name, string expected)
    {
        Assert.Equal(Enum.Parse<MediaTypeCategory>(expected), _registry.Classify(new(name)).Category);
    }

    [Fact]
    public void RegistryCanUseNonExtensionClassifierAheadOfDefaults()
    {
        var registry = new MediaTypeRegistry([new DeclaredTypeClassifier(),
            new ExtensionMediaTypeClassifier(MediaTypeCategory.Video, ".mp4")]);

        var result = registry.Classify(new("extensionless", "image/example"));

        Assert.Equal(MediaTypeCategory.StillImage, result.Category);
        Assert.Equal("declared-example", result.FormatKey);
    }

    private sealed class DeclaredTypeClassifier : IMediaTypeClassifier
    {
        public bool TryClassify(MediaTypeClassificationContext context, out MediaTypeClassification classification)
        {
            if (context.DeclaredContentType == "image/example")
            {
                classification = new(MediaTypeCategory.StillImage, "declared-example");
                return true;
            }
            classification = MediaTypeClassification.Unknown;
            return false;
        }
    }
}

public sealed class MediaFolderEnumeratorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-enumeration-").FullName;
    private readonly Guid _rootId = Guid.NewGuid();

    [Fact]
    public async Task EnumeratesMixedMediaFoldersWithNormalizedLogicalPaths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Subfolder"));
        Write("photo.jpg");
        Write("negative.CR3");
        Write("clip.mp4");
        Write("sound.wav");
        Write("notes.txt");
        var service = CreateEnumerator();

        var result = await service.EnumerateAsync(new(_rootId));

        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(6, result.Entries.Count);
        Assert.Equal(MediaTypeCategory.Unknown, result.Entries.Single(item => item.IsDirectory).MediaType.Category);
        Assert.Equal(MediaTypeCategory.StillImage, Find(result, "photo.jpg").MediaType.Category);
        Assert.Equal(MediaTypeCategory.RawImage, Find(result, "negative.CR3").MediaType.Category);
        Assert.Equal(MediaTypeCategory.Video, Find(result, "clip.mp4").MediaType.Category);
        Assert.Equal(MediaTypeCategory.Audio, Find(result, "sound.wav").MediaType.Category);
        Assert.Equal(MediaTypeCategory.Unknown, Find(result, "notes.txt").MediaType.Category);
        Assert.All(result.Entries, item =>
        {
            Assert.Equal(_rootId, item.RootId);
            Assert.DoesNotContain('\\', item.RelativePath);
            Assert.Equal(MediaPathSemantics.RelativePathKey(item.RelativePath), item.RelativePathKey);
        });
    }

    [Fact]
    public async Task EnumerationIsDeterministicWithDirectoriesFirst()
    {
        Directory.CreateDirectory(Path.Combine(_root, "z-folder"));
        Directory.CreateDirectory(Path.Combine(_root, "A-folder"));
        Write("zeta.mp4");
        Write("Alpha.mp4");
        Write("alpha.MOV");
        var service = CreateEnumerator();

        var first = await service.EnumerateAsync(new(_rootId));
        var second = await service.EnumerateAsync(new(_rootId));

        var expected = new[] { "A-folder", "z-folder", "alpha.MOV", "Alpha.mp4", "zeta.mp4" };
        Assert.Equal(expected, first.Entries.Select(item => item.Name));
        Assert.Equal(first.Entries.Select(item => item.RelativePath), second.Entries.Select(item => item.RelativePath));
    }

    [Fact]
    public async Task EnumeratesUnicodeAndLongContainedPaths()
    {
        var nested = string.Join('/', new string('深', 70), new string('階', 70), new string('層', 70));
        var physical = MediaPathSemantics.ResolveContained(_root, nested);
        Directory.CreateDirectory(physical);
        var name = $"été-東京-{new string('x', 80)}.jpg";
        File.WriteAllText(Path.Combine(physical, name), "image");

        var result = await CreateEnumerator().EnumerateAsync(new(_rootId, nested));

        var item = Assert.Single(result.Entries);
        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(name, item.Name);
        Assert.Equal(MediaTypeCategory.StillImage, item.MediaType.Category);
        Assert.Equal($"{nested}/{name}", item.RelativePath);
    }

    [Fact]
    public async Task HandlesLargeDirectoryWithoutUnboundedWorkAndSortsResults()
    {
        const int count = 2500;
        for (var index = count - 1; index >= 0; index--) Write($"clip-{index:D4}.mp4");

        var result = await CreateEnumerator().EnumerateAsync(new(_rootId));

        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(count, result.Entries.Count);
        Assert.Equal("clip-0000.mp4", result.Entries[0].Name);
        Assert.Equal("clip-2499.mp4", result.Entries[^1].Name);
        Assert.All(result.Entries, item => Assert.Equal(MediaTypeCategory.Video, item.MediaType.Category));
    }

    [Fact]
    public async Task ConcurrentEnumerationsAreBoundedWithoutStarvingQueuedWork()
    {
        var fileSystem = new ConcurrencyFileSystem(expectedMaximum: 2);
        var service = new MediaFolderEnumerator(
            new FakeRoots(new(_rootId, "Media", _root, MediaRootAvailability.Online)),
            MediaTypeRegistry.CreateDefault(), fileSystem, maximumConcurrency: 2);

        var enumerations = Task.WhenAll(
            service.EnumerateAsync(new(_rootId)),
            service.EnumerateAsync(new(_rootId)),
            service.EnumerateAsync(new(_rootId)));
        try
        {
            await fileSystem.ConcurrencyLimitReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, fileSystem.MaximumObserved);
        }
        finally { fileSystem.Release.TrySetResult(); }

        var results = await enumerations;

        Assert.All(results, result => Assert.True(result.Succeeded, result.Diagnostic));
        Assert.Equal(3, fileSystem.CallCount);
        Assert.Equal(2, fileSystem.MaximumObserved);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutReturningPartialSuccess()
    {
        var fileSystem = new BlockingFileSystem();
        var service = CreateEnumerator(fileSystem);
        using var cancellation = new CancellationTokenSource();
        var enumeration = service.EnumerateAsync(new(_rootId), cancellation.Token);
        await fileSystem.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enumeration);
    }

    [Fact]
    public async Task AccessDeniedReturnsExplicitDiagnostic()
    {
        var service = CreateEnumerator(new ThrowingFileSystem(new UnauthorizedAccessException("denied")));

        var result = await service.EnumerateAsync(new(_rootId));

        Assert.Equal(MediaFolderEnumerationStatus.AccessDenied, result.Status);
        Assert.Empty(result.Entries);
        Assert.Contains("denied", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransientFolderFailureReturnsUnavailableDiagnostic()
    {
        var service = CreateEnumerator(new ThrowingFileSystem(new IOException("network interruption")));

        var result = await service.EnumerateAsync(new(_rootId, "network-folder"));

        Assert.Equal(MediaFolderEnumerationStatus.FolderUnavailable, result.Status);
        Assert.Empty(result.Entries);
        Assert.Contains("network interruption", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Unavailable")]
    [InlineData("Unmapped")]
    public async Task UnavailableRootDoesNotTouchFilesystem(string availabilityName)
    {
        var availability = Enum.Parse<MediaRootAvailability>(availabilityName);
        var fileSystem = new RecordingFileSystem();
        var root = new MediaRootInfo(_rootId, "Media", availability == MediaRootAvailability.Unmapped ? null : _root,
            availability, "offline");
        var service = new MediaFolderEnumerator(new FakeRoots(root), MediaTypeRegistry.CreateDefault(), fileSystem);

        var result = await service.EnumerateAsync(new(_rootId));

        Assert.Equal(MediaFolderEnumerationStatus.RootUnavailable, result.Status);
        Assert.Equal(0, fileSystem.CallCount);
    }

    [Fact]
    public async Task MissingRootAndFolderAreReportedSeparately()
    {
        var missingRoot = new MediaFolderEnumerator(new FakeRoots(null), MediaTypeRegistry.CreateDefault(),
            new MediaFolderFileSystem());
        var missingFolder = CreateEnumerator();

        var rootResult = await missingRoot.EnumerateAsync(new(_rootId));
        var folderResult = await missingFolder.EnumerateAsync(new(_rootId, "missing"));

        Assert.Equal(MediaFolderEnumerationStatus.RootNotFound, rootResult.Status);
        Assert.Equal(MediaFolderEnumerationStatus.FolderNotFound, folderResult.Status);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("/outside")]
    public async Task InvalidFolderCannotEscapeMediaRoot(string relativeFolder)
    {
        var fileSystem = new RecordingFileSystem();
        var result = await CreateEnumerator(fileSystem).EnumerateAsync(new(_rootId, relativeFolder));

        Assert.Equal(MediaFolderEnumerationStatus.InvalidPath, result.Status);
        Assert.Equal(0, fileSystem.CallCount);
    }

    [Fact]
    public async Task FilesystemEntryOutsideRootIsRejected()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "outside.mp4");
        var fileSystem = new FixedFileSystem([new(outside, "outside.mp4", false, 1, DateTimeOffset.UtcNow)]);

        var result = await CreateEnumerator(fileSystem).EnumerateAsync(new(_rootId));

        Assert.Equal(MediaFolderEnumerationStatus.Failed, result.Status);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task LinkedDirectoryAndFileEntriesAreSkippedWithoutExposingOutsideMedia()
    {
        var linkedDirectory = Path.Combine(_root, "outside-folder-link");
        var linkedFile = Path.Combine(_root, "outside-file-link.mp4");
        var ordinaryDirectory = Path.Combine(_root, "ordinary-folder");
        var ordinaryFile = Path.Combine(_root, "ordinary.mp4");
        var fileSystem = new FixedFileSystem(
        [
            new(linkedDirectory, "outside-folder-link", true, null, default, IsReparsePoint: true),
            new(linkedFile, "outside-file-link.mp4", false, null, default, IsReparsePoint: true),
            new(ordinaryDirectory, "ordinary-folder", true, null, DateTimeOffset.UtcNow),
            new(ordinaryFile, "ordinary.mp4", false, 10, DateTimeOffset.UtcNow)
        ]);

        var result = await CreateEnumerator(fileSystem).EnumerateAsync(new(_rootId));

        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(2, result.SkippedLinkedEntries);
        Assert.Contains("filesystem link", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "ordinary-folder", "ordinary.mp4" }, result.Entries.Select(item => item.Name));
        Assert.Equal(MediaTypeCategory.Video, result.Entries.Single(item => !item.IsDirectory).MediaType.Category);
    }

    [Fact]
    public async Task FolderReachedThroughLinkIsRejectedBeforeItsTargetCanBeEnumerated()
    {
        var service = CreateEnumerator(new ThrowingFileSystem(
            new MediaFolderLinkException("linked folder targets outside the Media Root")));

        var result = await service.EnumerateAsync(new(_rootId, "outside-folder-link"));

        Assert.Equal(MediaFolderEnumerationStatus.LinkedPathRejected, result.Status);
        Assert.Empty(result.Entries);
        Assert.Contains("outside", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    private MediaFolderEnumerator CreateEnumerator(IMediaFolderFileSystem? fileSystem = null) => new(
        new FakeRoots(new(_rootId, "Media", _root, MediaRootAvailability.Online)),
        MediaTypeRegistry.CreateDefault(), fileSystem ?? new MediaFolderFileSystem());

    private static MediaFolderEntry Find(MediaFolderEnumerationResult result, string name) =>
        result.Entries.Single(item => item.Name == name);

    private void Write(string relativePath)
    {
        var path = MediaPathSemantics.ResolveContained(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "source");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeRoots(MediaRootInfo? root) : IMediaRootService
    {
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(root?.RootId == rootId ? root : null);
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaRootInfo>>(root is null ? [] : [root]);
        public Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BlockingFileSystem : IMediaFolderFileSystem
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(string mediaRootPath,
            string folderPath,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }

    private sealed class ThrowingFileSystem(Exception exception) : IMediaFolderFileSystem
    {
        public Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(string mediaRootPath,
            string folderPath,
            CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<MediaFolderFileSystemEntry>>(exception);
    }

    private sealed class RecordingFileSystem : IMediaFolderFileSystem
    {
        public int CallCount { get; private set; }
        public Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(string mediaRootPath,
            string folderPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<MediaFolderFileSystemEntry>>([]);
        }
    }

    private sealed class FixedFileSystem(IReadOnlyList<MediaFolderFileSystemEntry> entries) : IMediaFolderFileSystem
    {
        public Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(string mediaRootPath,
            string folderPath,
            CancellationToken cancellationToken = default) => Task.FromResult(entries);
    }

    private sealed class ConcurrencyFileSystem(int expectedMaximum) : IMediaFolderFileSystem
    {
        private int _active;
        private int _maximumObserved;
        private int _callCount;
        public TaskCompletionSource ConcurrencyLimitReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount => _callCount;
        public int MaximumObserved => _maximumObserved;

        public async Task<IReadOnlyList<MediaFolderFileSystemEntry>> EnumerateAsync(string mediaRootPath,
            string folderPath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            var active = Interlocked.Increment(ref _active);
            int observed;
            while ((observed = _maximumObserved) < active &&
                Interlocked.CompareExchange(ref _maximumObserved, active, observed) != observed) { }
            if (active >= expectedMaximum) ConcurrencyLimitReached.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return [];
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
