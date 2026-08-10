using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingOutputLifecycleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-output-").FullName;

    [Theory]
    [InlineData("clip.mp4", "clip.mp4.lightflow")]
    [InlineData("clip.mov", "clip.mov.lightflow")]
    [InlineData("clip.mkv", "clip.mkv.lightflow")]
    public void Constructor_DerivesExactSiblingPartialPath(string finalName, string partialName)
    {
        var lifecycle = new EncodingOutputLifecycle(Path.Combine(_root, finalName));

        Assert.Equal(Path.Combine(_root, partialName), lifecycle.PartialPath);
        Assert.True(EncodingOutputLifecycle.IsOwnedPartialPath(lifecycle.PartialPath));
        Assert.False(EncodingOutputLifecycle.IsOwnedPartialPath(Path.Combine(_root, "clip.lightflow.mp4")));
    }

    [Fact]
    public void Prepare_RemovesOnlyTheExactStalePartial()
    {
        var final = Path.Combine(_root, "clip.mp4");
        var lifecycle = new EncodingOutputLifecycle(final);
        var unrelated = Path.Combine(_root, "clip.other.lightflow");
        File.WriteAllText(lifecycle.PartialPath, "stale");
        File.WriteAllText(unrelated, "user file");

        lifecycle.Prepare();

        Assert.True(lifecycle.RemovedStalePartial);
        Assert.False(File.Exists(lifecycle.PartialPath));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void Prepare_BlocksWhenStalePartialCannotBeDeleted()
    {
        var final = Path.Combine(_root, "clip.mp4");
        var files = new FakeFiles(final + EncodingOutputLifecycle.PartialExtension) { DeleteException = new UnauthorizedAccessException("locked") };
        var lifecycle = new EncodingOutputLifecycle(final, files: files);

        var error = Assert.Throws<IOException>(() => lifecycle.Prepare());

        Assert.Contains("stale partial output", error.Message);
        Assert.False(files.MoveCalled);
        Assert.False(files.ReplaceCalled);
    }

    [Fact]
    public void FinalizeValidatedOutput_MovesNewPartialToFinal()
    {
        var final = Path.Combine(_root, "clip.mp4");
        var lifecycle = new EncodingOutputLifecycle(final);
        lifecycle.Prepare();
        File.WriteAllText(lifecycle.PartialPath, "validated");

        lifecycle.FinalizeValidatedOutput();

        Assert.Equal("validated", File.ReadAllText(final));
        Assert.False(File.Exists(lifecycle.PartialPath));
    }

    [Fact]
    public void FinalizeValidatedOutput_ReplacesOnlyAfterPartialIsReady()
    {
        var final = Path.Combine(_root, "clip.mp4");
        File.WriteAllText(final, "old-valid-output");
        var lifecycle = new EncodingOutputLifecycle(final);
        lifecycle.Prepare();
        Assert.Equal("old-valid-output", File.ReadAllText(final));
        File.WriteAllText(lifecycle.PartialPath, "new-validated-output");
        Assert.Equal("old-valid-output", File.ReadAllText(final));

        lifecycle.FinalizeValidatedOutput();

        Assert.Equal("new-validated-output", File.ReadAllText(final));
        Assert.False(File.Exists(lifecycle.PartialPath));
    }

    [Fact]
    public void CleanupFailedAttempt_RemovesPartialWithoutCreatingFinal()
    {
        var final = Path.Combine(_root, "cancelled.mp4");
        var lifecycle = new EncodingOutputLifecycle(final);
        lifecycle.Prepare();
        File.WriteAllText(lifecycle.PartialPath, "incomplete");

        var warning = lifecycle.CleanupFailedAttempt();

        Assert.Null(warning);
        Assert.False(File.Exists(lifecycle.PartialPath));
        Assert.False(File.Exists(final));
    }

    [Fact]
    public void FinalizationFailure_DoesNotDestroyExistingFinal()
    {
        var final = Path.Combine(_root, "clip.mp4");
        var partial = final + EncodingOutputLifecycle.PartialExtension;
        var files = new FakeFiles(final, partial) { ReplaceException = new IOException("replace failed") };
        var lifecycle = new EncodingOutputLifecycle(final, files: files);

        Assert.Throws<IOException>(() => lifecycle.FinalizeValidatedOutput());

        Assert.True(files.Exists(final));
        Assert.True(files.Exists(partial));
    }

    [Fact]
    public void CompletedItemFollowedByPreparationFailure_DoesNotCleanPreviousOutputOrIdentity()
    {
        var finalA = Path.Combine(_root, "item-a.mp4");
        var cache = Path.Combine(_root, "identity-cache");
        var identity = new EncodingOutputIdentity("source-a.mov", 123, 456, null, null, "options");
        EncodingOutputLifecycle? currentOutput = new EncodingOutputLifecycle(finalA, identityCacheDirectory: cache);
        currentOutput.Prepare();
        File.WriteAllText(currentOutput.PartialPath, "validated item A");
        currentOutput.FinalizeValidatedOutput();
        EncodingOutputIdentityStore.Save(finalA, identity, cache);

        // Item A has reached a terminal state and relinquishes lifecycle ownership before item B preparation.
        currentOutput = null;
        var blockedParent = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(blockedParent, "file");
        Assert.ThrowsAny<IOException>(() => Directory.CreateDirectory(Path.Combine(blockedParent, "item-b")));

        // This mirrors the outer exception handler: there is no active lifecycle to clean.
        currentOutput?.CleanupFailedAttempt();
        Assert.Equal("validated item A", File.ReadAllText(finalA));
        Assert.True(EncodingOutputIdentityStore.Matches(finalA, identity, cache));
    }

    [Fact]
    public void CleanupFailedAttempt_AfterSuccessfulFinalizationIsNoOp()
    {
        var final = Path.Combine(_root, "completed.mp4");
        var cache = Path.Combine(_root, "identity-cache");
        var identity = new EncodingOutputIdentity("source.mov", 1, 2, null, null, "options");
        var lifecycle = new EncodingOutputLifecycle(final, identityCacheDirectory: cache);
        lifecycle.Prepare();
        File.WriteAllText(lifecycle.PartialPath, "completed");
        lifecycle.FinalizeValidatedOutput();
        EncodingOutputIdentityStore.Save(final, identity, cache);

        Assert.Null(lifecycle.CleanupFailedAttempt());
        Assert.True(File.Exists(final));
        Assert.True(EncodingOutputIdentityStore.Matches(final, identity, cache));
    }

    [Fact]
    public void CleanupFailedAttempt_LeavesExistingFinalAndReportsPartialDeletionFailure()
    {
        var final = Path.Combine(_root, "clip.mp4");
        var partial = final + EncodingOutputLifecycle.PartialExtension;
        var files = new FakeFiles(final);
        var lifecycle = new EncodingOutputLifecycle(final, files: files);
        lifecycle.Prepare();
        files.Add(partial);
        files.DeleteException = new IOException("in use");

        var warning = lifecycle.CleanupFailedAttempt();

        Assert.Contains(partial, warning);
        Assert.True(files.Exists(final));
        Assert.True(files.Exists(partial));
    }

    [Fact]
    public void Constructor_RejectsSourceCollisionWithFinalOrPartial()
    {
        var final = Path.Combine(_root, "clip.mp4");
        Assert.Throws<InvalidOperationException>(() => new EncodingOutputLifecycle(final, final));
        Assert.Throws<InvalidOperationException>(() => new EncodingOutputLifecycle(final, final + EncodingOutputLifecycle.PartialExtension));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private sealed class FakeFiles(params string[] paths) : IEncodingOutputFileSystem
    {
        private readonly HashSet<string> _paths = new(paths, StringComparer.OrdinalIgnoreCase);
        public Exception? DeleteException { get; set; }
        public Exception? ReplaceException { get; set; }
        public bool MoveCalled { get; private set; }
        public bool ReplaceCalled { get; private set; }
        public bool Exists(string path) => _paths.Contains(path);
        public void Add(string path) => _paths.Add(path);
        public void Delete(string path)
        {
            if (DeleteException is not null) throw DeleteException;
            _paths.Remove(path);
        }
        public void Move(string source, string destination) { MoveCalled = true; _paths.Remove(source); _paths.Add(destination); }
        public void Replace(string source, string destination)
        {
            ReplaceCalled = true;
            if (ReplaceException is not null) throw ReplaceException;
            _paths.Remove(source);
            _paths.Add(destination);
        }
    }
}
