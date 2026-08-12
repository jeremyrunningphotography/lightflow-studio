using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class MediaRootTests : IDisposable
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), $"lightflow-roots-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(@"Events\2026\.\clip.MP4", "Events/2026/clip.MP4", "EVENTS/2026/CLIP.MP4")]
    [InlineData("Events//clip.mp4", "Events/clip.mp4", "EVENTS/CLIP.MP4")]
    public void RelativePaths_NormalizeWithStableWindowsKey(string input, string expected, string key)
    {
        Assert.Equal(expected, MediaPathSemantics.NormalizeRelativePath(input));
        Assert.Equal(key, MediaPathSemantics.RelativePathKey(input));
    }

    [Theory]
    [InlineData(@"C:\video.mp4")]
    [InlineData(@"\\server\share\video.mp4")]
    [InlineData("../video.mp4")]
    [InlineData("folder/../../video.mp4")]
    [InlineData(".")]
    public void RelativePaths_RejectUnsafeValues(string input) =>
        Assert.Throws<ArgumentException>(() => MediaPathSemantics.NormalizeRelativePath(input));

    [Fact]
    public void RootOverlap_IsSegmentAwareAndCaseInsensitive()
    {
        Assert.True(MediaPathSemantics.Overlaps(@"C:\Lightflow", @"c:\lightflow\Media"));
        Assert.False(MediaPathSemantics.Overlaps(@"C:\Lightflow", @"C:\Lightflow2"));
    }

    [Fact]
    public async Task CreateRenameRemap_PreservesLogicalRootIdentity()
    {
        Directory.CreateDirectory(_temporary);
        var first = Directory.CreateDirectory(Path.Combine(_temporary, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_temporary, "second")).FullName;
        await using var fixture = await Fixture.CreateAsync(_temporary);

        var created = await fixture.Service.CreateAsync("Camera Originals", first);
        var renamed = await fixture.Service.RenameAsync(created.Root!.RootId, "Primary Originals");
        var remapped = await fixture.Service.RemapAsync(created.Root.RootId, second);

        Assert.True(created.Succeeded); Assert.True(renamed.Succeeded); Assert.True(remapped.Succeeded);
        Assert.Equal(created.Root.RootId, remapped.Root!.RootId);
        Assert.Equal("Primary Originals", remapped.Root.DisplayName);
        Assert.Equal(Path.GetFullPath(second), remapped.Root.PhysicalPath);
    }

    [Fact]
    public async Task Remap_DoesNotRewriteExistingAssets()
    {
        var first = Directory.CreateDirectory(Path.Combine(_temporary, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_temporary, "second")).FullName;
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var root = (await fixture.Service.CreateAsync("Originals", first)).Root!;
        using (var connection = fixture.Session.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO MediaAssets
                    (AssetId,RootId,RelativePath,RelativePathKey,MediaType,FileSizeBytes,LastWriteUtcTicks,SourceStatus,CreatedUtc,UpdatedUtc)
                VALUES ($asset,$root,'Day One/Clip.mp4','DAY ONE/CLIP.MP4','video',100,200,'available',$now,$now);
                """;
            command.Parameters.AddWithValue("$asset", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$root", root.RootId.ToString("D"));
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        Assert.True((await fixture.Service.RemapAsync(root.RootId, second)).Succeeded);

        using var verify = fixture.Session.OpenConnection();
        using var query = verify.CreateCommand();
        query.CommandText = "SELECT RootId || ':' || RelativePath || ':' || RelativePathKey FROM MediaAssets;";
        Assert.Equal($"{root.RootId:D}:Day One/Clip.mp4:DAY ONE/CLIP.MP4", Convert.ToString(query.ExecuteScalar()));
    }

    [Fact]
    public async Task Create_RejectsEquivalentAndNestedMappingsOnThisMachine()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_temporary, "media")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(parent, "child")).FullName;
        await using var fixture = await Fixture.CreateAsync(_temporary);
        Assert.True((await fixture.Service.CreateAsync("First", parent)).Succeeded);

        Assert.False((await fixture.Service.CreateAsync("Equivalent", parent + Path.DirectorySeparatorChar)).Succeeded);
        Assert.False((await fixture.Service.CreateAsync("Nested", child)).Succeeded);
        Assert.Single(await fixture.Service.ListAsync());
    }

    [Fact]
    public async Task ConcurrentEquivalentCreates_CommitOnlyOneMapping()
    {
        var path = Directory.CreateDirectory(Path.Combine(_temporary, "media")).FullName;
        await using var fixture = await Fixture.CreateAsync(_temporary);

        var results = await Task.WhenAll(
            fixture.Service.CreateAsync("First", path),
            fixture.Service.CreateAsync("Second", path.ToUpperInvariant()));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(await fixture.Service.ListAsync());
    }

    [Fact]
    public async Task OfflineRoot_IsDistinctFromMissingChild_AndDoesNotDeleteMapping()
    {
        var rootPath = Directory.CreateDirectory(Path.Combine(_temporary, "media")).FullName;
        var file = Path.Combine(rootPath, "present.mp4"); File.WriteAllText(file, "video");
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var root = (await fixture.Service.CreateAsync("Media", rootPath)).Root!;

        var missing = await fixture.Service.ResolveAsync(root.RootId, "missing.mp4");
        Assert.Equal(MediaRootAvailability.Online, missing.RootAvailability);
        Assert.False(missing.Exists); Assert.NotNull(missing.PhysicalPath);

        Directory.Delete(rootPath, recursive: true);
        var offline = await fixture.Service.ResolveAsync(root.RootId, "present.mp4");
        Assert.Equal(MediaRootAvailability.Unavailable, offline.RootAvailability);
        Assert.Null(offline.PhysicalPath);
        var persisted = await fixture.Service.GetAsync(root.RootId);
        Assert.Equal(rootPath, persisted!.PhysicalPath);
    }

    [Fact]
    public async Task DifferentMachine_SeesLogicalRootAsUnmapped()
    {
        var rootPath = Directory.CreateDirectory(Path.Combine(_temporary, "media")).FullName;
        await using var fixture = await Fixture.CreateAsync(_temporary, "machine-a");
        var created = await fixture.Service.CreateAsync("Shared Catalog Root", rootPath);
        var other = new MediaRootService(() => fixture.Session, new FixedMachine("machine-b"), new MediaRootFileSystem());

        var observed = await other.GetAsync(created.Root!.RootId);

        Assert.Equal(MediaRootAvailability.Unmapped, observed!.Availability);
        Assert.Null(observed.PhysicalPath);
    }

    [Fact]
    public void MachineIdentity_IsStableAndContainsNoHostIdentity()
    {
        var path = Path.Combine(_temporary, "machine-id");
        var first = new MachineIdentityProvider(path).GetMachineId();
        var second = new MachineIdentityProvider(path).GetMachineId();
        Assert.Equal(first, second);
        Assert.True(Guid.TryParse(first, out _));
        Assert.DoesNotContain(Environment.MachineName, first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MachineIdentity_ExistingValidIdentityIsReturnedWithoutChangingFile()
    {
        Directory.CreateDirectory(_temporary);
        var path = Path.Combine(_temporary, "machine-id");
        var identity = Guid.NewGuid();
        var bytes = System.Text.Encoding.UTF8.GetBytes($" {identity:D}\r\n");
        File.WriteAllBytes(path, bytes);

        Assert.Equal(identity.ToString("D"), new MachineIdentityProvider(path).GetMachineId());
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void MachineIdentity_ExistingMalformedIdentityFailsAndPreservesBytes()
    {
        Directory.CreateDirectory(_temporary);
        var path = Path.Combine(_temporary, "machine-id");
        byte[] bytes = [0xff, 0xfe, 0x00, 0x61, 0x62];
        File.WriteAllBytes(path, bytes);

        var exception = Assert.Throws<MachineIdentityException>(() => new MachineIdentityProvider(path).GetMachineId());

        Assert.Equal(MachineIdentityFailure.Malformed, exception.Failure);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void MachineIdentity_ExistingUnreadableIdentityFailsWithoutReplacement()
    {
        Directory.CreateDirectory(_temporary);
        var path = Path.Combine(_temporary, "machine-id");
        var identity = Guid.NewGuid().ToString("D");
        File.WriteAllText(path, identity);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var exception = Assert.Throws<MachineIdentityException>(() => new MachineIdentityProvider(path).GetMachineId());

        Assert.Equal(MachineIdentityFailure.Unavailable, exception.Failure);
        locked.Position = 0;
        using var reader = new StreamReader(locked, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
        Assert.Equal(identity, reader.ReadToEnd());
    }

    [Fact]
    public async Task MachineIdentity_ConcurrentFirstRunConvergesWithoutTemporaryFiles()
    {
        Directory.CreateDirectory(_temporary);
        var path = Path.Combine(_temporary, "machine-id");
        var providers = Enumerable.Range(0, 20).Select(_ => new MachineIdentityProvider(path)).ToArray();

        var identities = await Task.WhenAll(providers.Select(provider => Task.Run(provider.GetMachineId)));

        Assert.Single(identities.Distinct(StringComparer.Ordinal));
        Assert.True(Guid.TryParse(File.ReadAllText(path), out _));
        Assert.Empty(Directory.EnumerateFiles(_temporary, "machine-id.*.tmp"));
    }

    [Fact]
    public async Task CorruptIdentity_FailsExplicitlyWithoutChangingExistingMapping()
    {
        Directory.CreateDirectory(_temporary);
        var identityPath = Path.Combine(_temporary, "machine-id");
        var provider = new MachineIdentityProvider(identityPath);
        var established = provider.GetMachineId();
        var rootPath = Directory.CreateDirectory(Path.Combine(_temporary, "media")).FullName;
        await using var fixture = await Fixture.CreateAsync(_temporary, established);
        var root = (await fixture.Service.CreateAsync("Originals", rootPath)).Root!;
        File.WriteAllText(identityPath, "corrupt identity");
        var restarted = new MediaRootService(() => fixture.Session,
            new MachineIdentityProvider(identityPath), new MediaRootFileSystem());

        var exception = await Assert.ThrowsAsync<MachineIdentityException>(() => restarted.GetAsync(root.RootId));

        Assert.Equal(MachineIdentityFailure.Malformed, exception.Failure);
        using var connection = fixture.Session.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MachineId || ':' || PhysicalPath FROM MediaRootMappings WHERE RootId=$root;";
        command.Parameters.AddWithValue("$root", root.RootId.ToString("D"));
        Assert.Equal($"{established}:{rootPath}", Convert.ToString(command.ExecuteScalar()));
        Assert.Equal("corrupt identity", File.ReadAllText(identityPath));
    }

    public void Dispose() { try { Directory.Delete(_temporary, recursive: true); } catch { } }

    private sealed class FixedMachine(string id) : IMachineIdentityProvider { public string GetMachineId() => id; }

    private sealed class Fixture(CatalogDatabaseSession session, MediaRootService service) : IAsyncDisposable
    {
        public CatalogDatabaseSession Session { get; } = session;
        public MediaRootService Service { get; } = service;
        public static async Task<Fixture> CreateAsync(string root, string machine = "test-machine")
        {
            var locations = LightflowStorageLocations.Create(Path.Combine(root, "app"));
            var opened = await new CatalogDatabaseService(locations).CreateNewAsync();
            var session = opened.Session!;
            return new(session, new MediaRootService(() => session, new FixedMachine(machine), new MediaRootFileSystem()));
        }
        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }
}
