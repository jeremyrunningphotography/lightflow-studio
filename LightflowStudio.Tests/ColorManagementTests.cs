using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ColorManagementTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-color-{Guid.NewGuid():N}");
    private LightflowStorageCoordinator _storage = null!;
    private TestConfiguration _configuration = null!;
    private string _luts = null!;
    private Guid _assetId;

    [Fact]
    public void CubeRuntimeData_LoadsValidatedSamples()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "runtime.cube");
        File.WriteAllBytes(path, Cube(2, 0));
        var data = CubeLutData.Load(path);
        Assert.Equal(2, data.Size);
        Assert.Equal(32, data.Samples.Length);
        Assert.Equal([0f, .1f, .2f, 1f], data.Samples[..4]);
        Assert.Equal([7f, 7.1f, 7.2f, 1f], data.Samples[^4..]);
    }

    [Fact]
    public void Validation_RequiresSupportedThreeDimensionalCubeStructure()
    {
        var cube = CubeLutValidator.Validate(Cube(2, 0));
        Assert.True(cube.IsValid, cube.Diagnostic);
        Assert.Contains("1D", CubeLutValidator.Validate("LUT_1D_SIZE 2\n0 0 0\n1 1 1\n"u8).Diagnostic!);
        Assert.Contains("missing", CubeLutValidator.Validate("0 0 0\n"u8).Diagnostic!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("declares", CubeLutValidator.Validate("LUT_3D_SIZE 2\n0 0 0\n"u8).Diagnostic!,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(CubeLutValidator.Validate([0xff, 0xfe]).IsValid);
        Assert.Contains("DOMAIN_MAX", CubeLutValidator.Validate("LUT_3D_SIZE 2\nDOMAIN_MIN 1 0 0\nDOMAIN_MAX 0 1 1\n"u8).Diagnostic!);
    }

    [Fact]
    public async Task FolderRefresh_RegistersCompatibleFilesAndPersistsIdentityAcrossRestart()
    {
        var path = WriteCube(_luts, "Camera-Log.cube", 0);
        File.WriteAllText(Path.Combine(_luts, "ignore.txt"), "ignored");

        var first = await _storage.Luts.RefreshAsync(_luts);
        var resource = Assert.Single(first.Resources);
        await RestartAsync();
        var restored = Assert.Single((await _storage.Luts.RefreshAsync(_luts)).Resources);

        Assert.Equal(resource.LutId, restored.LutId);
        Assert.Equal(resource.ContentSha256, restored.ContentSha256);
        Assert.Equal(path, restored.FilePath);
    }

    [Fact]
    public async Task OverlappingFolderRefreshesReconcileOneStableResourceWithoutDatabaseRaces()
    {
        WriteCube(_luts, "Camera.cube", 0);

        var snapshots = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => _storage.Luts.RefreshAsync(_luts)));

        var expected = Assert.Single(snapshots[0].Resources).LutId;
        Assert.All(snapshots, snapshot => Assert.Equal(expected, Assert.Single(snapshot.Resources).LutId));
        using var connection = new CatalogSqliteConnectionFactory(_storage.CatalogSession.DatabasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM LutResources;";
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public async Task FolderRefresh_SurfacesInvalidFilesInHumanTerms()
    {
        File.WriteAllText(Path.Combine(_luts, "broken.cube"), "not a cube");
        WriteCube(_luts, "valid.cube", 0);

        var snapshot = await _storage.Luts.RefreshAsync(_luts);

        Assert.Single(snapshot.Resources);
        var problem = Assert.Single(snapshot.Problems);
        Assert.Equal("broken.cube", problem.FileName);
        Assert.Contains("three finite numbers", problem.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateContentCollapsesWhileDuplicateDisplayNamesRemainDistinct()
    {
        WriteCube(_luts, "Same-Name.cube", 0);
        WriteCube(_luts, "Same_Name.cube", 1);
        WriteCube(_luts, "Z Copy.cube", 0);

        var resources = (await _storage.Luts.RefreshAsync(_luts)).Resources;
        var options = LutCatalog.Options(resources);

        Assert.Equal(2, resources.Count);
        Assert.Equal(2, resources.Select(resource => resource.ContentSha256).Distinct().Count());
        Assert.Equal(["No LUT", "Same Name (1)", "Same Name (2)"], options.Select(option => option.DisplayName));
        Assert.NotEqual(options[1].LutId, options[2].LutId);
    }

    [Fact]
    public async Task RenamePreservesStableIdentityAndAssignment()
    {
        var original = WriteCube(_luts, "Technical.cube", 0);
        var resource = Assert.Single((await RefreshCacheAsync(_luts)).Resources);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, resource.LutId);
        var before = await _storage.AssetColors.GetAsync(_assetId);
        var renamed = Path.Combine(_luts, "Camera Transform.cube");
        File.Move(original, renamed);

        var refreshed = Assert.Single((await RefreshCacheAsync(_luts)).Resources);
        var after = await _storage.AssetColors.GetAsync(_assetId);

        Assert.Equal(resource.LutId, refreshed.LutId);
        Assert.Equal(resource.LutId, after.Camera!.LutId);
        Assert.Equal("Camera Transform", after.Camera.DisplayName);
        Assert.Equal(LutResourceAvailability.Available, after.Camera.Availability);
        Assert.Equal(before.ColorIdentity, after.ColorIdentity);
    }

    [Fact]
    public async Task RemovedOrChangedFileDoesNotSilentlySubstituteAssignment()
    {
        var path = WriteCube(_luts, "Technical.cube", 0);
        var original = Assert.Single((await RefreshCacheAsync(_luts)).Resources);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, original.LutId);

        File.Delete(path);
        await RefreshCacheAsync(_luts);
        var removed = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal(LutResourceAvailability.Missing, removed.Camera!.Availability);
        Assert.Throws<FileNotFoundException>(() => _storage.LutCache.ResolvePath(ColorLutStage.Camera, original.LutId));

        WriteCube(_luts, "Technical.cube", 1);
        var replacement = Assert.Single((await RefreshCacheAsync(_luts)).Resources);
        var changed = await _storage.AssetColors.GetAsync(_assetId);
        Assert.NotEqual(original.LutId, replacement.LutId);
        Assert.Equal(original.LutId, changed.Camera!.LutId);
        Assert.Equal(LutResourceAvailability.Missing, changed.Camera.Availability);
    }

    [Fact]
    public async Task FolderChangePreservesAssignmentOnlyWhenSameContentExists()
    {
        WriteCube(_luts, "Original Name.cube", 0);
        var resource = Assert.Single((await RefreshCacheAsync(_luts)).Resources);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, resource.LutId);
        var other = Directory.CreateDirectory(Path.Combine(_root, "other-luts")).FullName;
        SetLutFolder(other);
        await _storage.LutCache.RefreshAsync(ColorLutStage.Camera, other);
        Assert.Equal(LutResourceAvailability.Missing, (await _storage.AssetColors.GetAsync(_assetId)).Camera!.Availability);

        WriteCube(other, "Different Name.cube", 0);
        var matched = Assert.Single((await _storage.LutCache.RefreshAsync(ColorLutStage.Camera, other)).Resources);
        var restored = await _storage.AssetColors.GetAsync(_assetId);

        Assert.Equal(resource.LutId, matched.LutId);
        Assert.Equal(LutResourceAvailability.Available, restored.Camera!.Availability);
        Assert.Equal("Different Name", restored.Camera.DisplayName);
    }

    [Fact]
    public async Task RecursiveRootsPreserveNestedIdentityAndDeduplicateContent()
    {
        var cameraRoot = Directory.CreateDirectory(Path.Combine(_root, "recursive-camera")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(cameraRoot, "DJI", "Log Conversions")).FullName;
        var originalPath = WriteCube(nested, "Shared Name.cube", 0);
        WriteCube(nested, "Invalid.cube", 0);
        File.WriteAllText(Path.Combine(nested, "Invalid.cube"), "not a cube");
        var first = await _storage.Luts.RefreshAsync(cameraRoot, true);
        var original = Assert.Single(first.Resources);
        Assert.Single(first.Problems);

        var movedFolder = Directory.CreateDirectory(Path.Combine(cameraRoot, "Moved")).FullName;
        var movedPath = Path.Combine(movedFolder, "Renamed.cube");
        File.Move(originalPath, movedPath);
        WriteCube(Path.Combine(cameraRoot, "Duplicate"), "Copy.cube", 0);
        WriteCube(Path.Combine(cameraRoot, "Different"), "Renamed.cube", 1);
        var refreshed = await _storage.Luts.RefreshAsync(cameraRoot, true);

        Assert.Equal(2, refreshed.Resources.Count);
        Assert.Contains(refreshed.Resources, resource => resource.LutId == original.LutId);
        Assert.True(File.Exists(refreshed.Resources.Single(resource => resource.LutId == original.LutId).FilePath));
        Assert.Equal(2, refreshed.Resources.Select(resource => resource.LutId).Distinct().Count());
        Assert.Equal(2, LutCatalog.CombinedOptions(refreshed.Resources, refreshed.Resources).Count - 1);
    }

    [Fact]
    public async Task IncludeSubfoldersControlsStageDiscovery()
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "optional-recursion")).FullName;
        WriteCube(root, "Top.cube", 0);
        WriteCube(Path.Combine(root, "Nested"), "Nested.cube", 1);

        var topOnly = await _storage.Luts.RefreshAsync(root, false);
        Assert.Single(topOnly.Resources);
        Assert.Equal("Top", topOnly.Resources[0].DisplayName);

        var recursive = await _storage.Luts.RefreshAsync(root, true);
        Assert.Equal(2, recursive.Resources.Count);
        Assert.Contains(recursive.Resources, resource => resource.DisplayName == "Nested");
    }

    [Fact]
    public async Task StageFoldersRemainSeparatedWhileEncodingUsesIdentityDeduplicatedUnion()
    {
        var cameraFolder = Directory.CreateDirectory(Path.Combine(_root, "camera-luts")).FullName;
        var creativeFolder = Directory.CreateDirectory(Path.Combine(_root, "creative-luts")).FullName;
        WriteCube(cameraFolder, "Shared Name.cube", 0);
        WriteCube(creativeFolder, "Shared Name.cube", 1); // same name, different content
        WriteCube(cameraFolder, "Duplicate Camera.cube", 2);
        WriteCube(creativeFolder, "Duplicate Creative.cube", 2); // duplicate content, different name/path
        _storage.SaveSettings(_storage.Settings with
        {
            CameraLutFolder = cameraFolder,
            CreativeLutFolder = creativeFolder
        });

        var camera = await _storage.LutCache.RefreshAsync(ColorLutStage.Camera, cameraFolder);
        var creative = await _storage.LutCache.RefreshAsync(ColorLutStage.Creative, creativeFolder);
        Assert.Equal(2, camera.Resources.Count);
        Assert.Equal(2, creative.Resources.Count);
        Assert.NotEqual(camera.Resources.Single(x => x.DisplayName == "Shared Name").LutId,
            creative.Resources.Single(x => x.DisplayName == "Shared Name").LutId);
        Assert.Equal(camera.Resources.Single(x => x.DisplayName == "Duplicate Camera").LutId,
            creative.Resources.Single(x => x.DisplayName == "Duplicate Creative").LutId);

        var encoding = LutCatalog.CombinedOptions(camera.Resources, creative.Resources).Skip(1).ToArray();
        Assert.Equal(3, encoding.Length);
        Assert.Equal(3, encoding.Select(option => option.LutId).Distinct().Count());

        var cameraOnly = camera.Resources.Single(x => x.DisplayName == "Shared Name");
        var creativeOnly = creative.Resources.Single(x => x.DisplayName == "Shared Name");
        await _storage.AssetColors.SetAsync([new(_assetId, cameraOnly.LutId, creativeOnly.LutId)]);
        var assigned = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal(LutResourceAvailability.Available, assigned.Camera!.Availability);
        Assert.Equal(LutResourceAvailability.Available, assigned.Creative!.Availability);

        _storage.SaveSettings(_storage.Settings with { CameraLutFolder = _luts });
        await _storage.LutCache.RefreshAsync(ColorLutStage.Camera, _luts);
        assigned = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal(LutResourceAvailability.Missing, assigned.Camera!.Availability);
        Assert.Equal(LutResourceAvailability.Available, assigned.Creative!.Availability);
    }

    [Fact]
    public async Task ApplicationCacheIsTheOnlyScannerForAssignmentsResolutionAndEncoding()
    {
        var cameraFolder = Directory.CreateDirectory(Path.Combine(_root, "cached-camera")).FullName;
        var creativeFolder = Directory.CreateDirectory(Path.Combine(_root, "cached-creative")).FullName;
        WriteCube(cameraFolder, "Camera.cube", 0);
        WriteCube(creativeFolder, "Creative.cube", 1);
        var scanner = new CountingLutLibrary(_storage.Luts);
        using var cache = new ApplicationLutLibraryCache(scanner);

        await cache.InitializeAsync(cameraFolder, creativeFolder);
        Assert.Equal(1, scanner.Count(cameraFolder));
        Assert.Equal(1, scanner.Count(creativeFolder));
        Assert.Contains((cameraFolder, false), scanner.Calls);
        Assert.Contains((creativeFolder, false), scanner.Calls);
        var camera = Assert.Single(cache.Snapshot(ColorLutStage.Camera).Resources);
        var creative = Assert.Single(cache.Snapshot(ColorLutStage.Creative).Resources);
        var colors = new CatalogAssetColorStore(() => _storage.CatalogSession, cache);
        await colors.SetAsync([new(_assetId, camera.LutId, creative.LutId)]);

        for (var index = 0; index < 100; index++)
        {
            var intent = await colors.GetAsync(_assetId);
            Assert.Equal(LutResourceAvailability.Available, intent.Camera!.Availability);
            Assert.Equal(LutResourceAvailability.Available, intent.Creative!.Availability);
            Assert.Equal(camera.FilePath, cache.ResolvePath(ColorLutStage.Camera, camera.LutId));
            Assert.Equal(creative.FilePath, cache.ResolvePath(ColorLutStage.Creative, creative.LutId));
            Assert.Equal(2, LutCatalog.CombinedOptions(cache.Snapshot(ColorLutStage.Camera).Resources,
                cache.Snapshot(ColorLutStage.Creative).Resources).Count - 1);
        }
        Assert.Equal(1, scanner.Count(cameraFolder));
        Assert.Equal(1, scanner.Count(creativeFolder));

        var replacementCamera = Directory.CreateDirectory(Path.Combine(_root, "replacement-camera")).FullName;
        await cache.RefreshAsync(ColorLutStage.Camera, replacementCamera);
        Assert.Equal(1, scanner.Count(replacementCamera));
        Assert.Equal(1, scanner.Count(creativeFolder));
        Assert.Equal(LutResourceAvailability.Missing, (await colors.GetAsync(_assetId)).Camera!.Availability);
        await cache.RefreshAsync(ColorLutStage.Camera, cameraFolder, true);
        Assert.Contains((cameraFolder, true), scanner.Calls);
        Assert.Equal(1, scanner.Count(creativeFolder));
        Assert.Equal(LutResourceAvailability.Available, (await colors.GetAsync(_assetId)).Camera!.Availability);
    }

    [Fact]
    public async Task RuntimeCacheParsesOncePerLutIdAndInvalidatesOnlyRemovedContent()
    {
        var cameraFolder = Directory.CreateDirectory(Path.Combine(_root, "runtime-camera")).FullName;
        var creativeFolder = Directory.CreateDirectory(Path.Combine(_root, "runtime-creative")).FullName;
        var originalPath = WriteCube(cameraFolder, "Cached.cube", 0);
        var parseCount = 0;
        using var cache = new ApplicationLutLibraryCache(_storage.Luts, path =>
        {
            parseCount++;
            return CubeLutData.Load(path);
        });
        await cache.InitializeAsync(cameraFolder, creativeFolder);
        var lut = Assert.Single(cache.Snapshot(ColorLutStage.Camera).Resources);

        Assert.Same(await cache.GetRuntimeAsync(ColorLutStage.Camera, lut.LutId),
            await cache.GetRuntimeAsync(ColorLutStage.Camera, lut.LutId));
        Assert.Equal(1, parseCount);

        var movedPath = Path.Combine(cameraFolder, "nested", "Moved.cube");
        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        File.Move(originalPath, movedPath);
        await cache.RefreshAsync(ColorLutStage.Camera, cameraFolder, true);
        await cache.GetRuntimeAsync(ColorLutStage.Camera, lut.LutId);
        Assert.Equal(1, parseCount); // Stable content identity survives a path move.

        File.Delete(movedPath);
        await cache.RefreshAsync(ColorLutStage.Camera, cameraFolder);
        Assert.Throws<FileNotFoundException>(() => cache.ResolvePath(ColorLutStage.Camera, lut.LutId));
        WriteCube(cameraFolder, "Returned.cube", 0);
        await cache.RefreshAsync(ColorLutStage.Camera, cameraFolder);
        await cache.GetRuntimeAsync(ColorLutStage.Camera, lut.LutId);
        Assert.Equal(2, parseCount);
    }

    [Fact]
    public async Task TypedAssignmentsSupportOrderedStagesIndependentClearingAndStableAssetIdentity()
    {
        WriteCube(_luts, "Camera.cube", 0);
        WriteCube(_luts, "Creative.cube", 1);
        var resources = (await RefreshCacheAsync(_luts)).Resources;
        var camera = resources.Single(resource => resource.DisplayName == "Camera");
        var creative = resources.Single(resource => resource.DisplayName == "Creative");
        var original = await _storage.AssetColors.GetAsync(_assetId);

        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, camera.LutId);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Creative, creative.LutId);
        var both = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal([camera.LutId, creative.LutId], both.OrderedPipeline.Select(item => item.LutId));
        Assert.NotEqual(original.ColorIdentity, both.ColorIdentity);

        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, null);
        var creativeOnly = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Null(creativeOnly.Camera);
        Assert.Equal(creative.LutId, creativeOnly.Creative!.LutId);

        var root = Assert.Single(await _storage.MediaRoots.ListAsync());
        var moved = Directory.CreateDirectory(Path.Combine(_root, "moved-media")).FullName;
        Assert.True((await _storage.MediaRoots.RemapAsync(root.RootId, moved)).Succeeded);
        await RestartAsync();
        Assert.Equal(creative.LutId, (await _storage.AssetColors.GetAsync(_assetId)).Creative!.LutId);
    }

    [Fact]
    public async Task ColorEnabled_IsPerAssetIndependentOfAssignmentsAndSurvivesRestart()
    {
        WriteCube(_luts, "Camera.cube", 0);
        WriteCube(_luts, "Creative.cube", 1);
        var resources = (await RefreshCacheAsync(_luts)).Resources;
        var camera = resources.Single(resource => resource.DisplayName == "Camera");
        var creative = resources.Single(resource => resource.DisplayName == "Creative");
        var root = Assert.Single(await _storage.MediaRoots.ListAsync());
        File.WriteAllText(Path.Combine(_root, "media", "second.mp4"), "media");
        var secondAssetId = (await _storage.MediaAssets.CreateAsync(root.RootId, "second.mp4", "video")).Asset!.Asset.AssetId;

        Assert.False((await _storage.AssetColors.GetAsync(_assetId)).ColorEnabled);
        Assert.False((await _storage.AssetColors.GetAsync(secondAssetId)).ColorEnabled);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, camera.LutId);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Creative, creative.LutId);
        var beforeToggle = await _storage.AssetColors.GetAsync(_assetId);

        await _storage.AssetColors.SetColorEnabledAsync([_assetId], true);
        var enabled = await _storage.AssetColors.GetAsync(_assetId);
        Assert.True(enabled.ColorEnabled);
        Assert.Equal(camera.LutId, enabled.Camera!.LutId);
        Assert.Equal(creative.LutId, enabled.Creative!.LutId);
        Assert.NotEqual(beforeToggle.ColorIdentity, enabled.ColorIdentity);
        Assert.False((await _storage.AssetColors.GetAsync(secondAssetId)).ColorEnabled);

        await RestartAsync();
        Assert.True((await _storage.AssetColors.GetAsync(_assetId)).ColorEnabled);
        Assert.False((await _storage.AssetColors.GetAsync(secondAssetId)).ColorEnabled);

        await _storage.AssetColors.SetColorEnabledAsync([_assetId], false);
        await RestartAsync();
        var disabled = await _storage.AssetColors.GetAsync(_assetId);
        Assert.False(disabled.ColorEnabled);
        Assert.Equal(camera.LutId, disabled.Camera!.LutId);
        Assert.Equal(creative.LutId, disabled.Creative!.LutId);
    }

    [Fact]
    public async Task BulkAssignmentFailureIsAtomicAndColorIdentityIgnoresUnrelatedMetadata()
    {
        WriteCube(_luts, "Camera.cube", 0);
        var lut = Assert.Single((await RefreshCacheAsync(_luts)).Resources);
        var original = await _storage.AssetColors.GetAsync(_assetId);
        Execute("UPDATE MediaAssets SET MediaType='image',UpdatedUtc=$now WHERE AssetId=$id;",
            ("$now", DateTime.UtcNow.ToString("O")), ("$id", _assetId.ToString("D")));
        Assert.Equal(original.ColorIdentity, (await _storage.AssetColors.GetAsync(_assetId)).ColorIdentity);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _storage.AssetColors.SetAsync([
            new(_assetId, lut.LutId, null),
            new(Guid.NewGuid(), lut.LutId, null)
        ]));
        Assert.False((await _storage.AssetColors.GetAsync(_assetId)).HasColor);
    }

    public async Task InitializeAsync()
    {
        _luts = Directory.CreateDirectory(Path.Combine(_root, "luts")).FullName;
        _configuration = new(new AppSettings(_luts));
        _storage = (await LightflowStorageCoordinator.StartAsync(_root, configuration: _configuration)).Coordinator!;
        await _storage.LutCache.InitializeAsync(_storage.Settings.CameraLutFolder, _storage.Settings.CreativeLutFolder);
        var media = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
        File.WriteAllText(Path.Combine(media, "clip.mp4"), "media");
        var root = (await _storage.MediaRoots.CreateAsync("Media", media)).Root!;
        _assetId = (await _storage.MediaAssets.CreateAsync(root.RootId, "clip.mp4", "video")).Asset!.Asset.AssetId;
    }

    public async Task DisposeAsync()
    {
        await _storage.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
    }

    private async Task RestartAsync()
    {
        await _storage.DisposeAsync();
        _storage = (await LightflowStorageCoordinator.StartAsync(_root, configuration: _configuration)).Coordinator!;
        await _storage.LutCache.InitializeAsync(_storage.Settings.CameraLutFolder, _storage.Settings.CreativeLutFolder);
    }

    private async Task<LutLibrarySnapshot> RefreshCacheAsync(string folder)
    {
        var camera = await _storage.LutCache.RefreshAsync(ColorLutStage.Camera, folder);
        await _storage.LutCache.RefreshAsync(ColorLutStage.Creative, folder);
        return camera;
    }

    private void SetLutFolder(string folder)
    {
        var settings = _storage.Settings with { CameraLutFolder = folder };
        _storage.SaveSettings(settings);
    }

    private static string WriteCube(string folder, string name, int offset)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Cube(2, offset));
        return path;
    }

    private static byte[] Cube(int size, int offset)
    {
        var rows = Enumerable.Range(0, size * size * size)
            .Select(index => $"{index + offset}.0 {index + offset}.1 {index + offset}.2");
        return System.Text.Encoding.UTF8.GetBytes(
            $"TITLE \"Test\"\nLUT_3D_SIZE {size}\nDOMAIN_MIN 0 0 0\nDOMAIN_MAX 1 1 1\n{string.Join('\n', rows)}\n");
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new CatalogSqliteConnectionFactory(_storage.CatalogSession.DatabasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    private sealed class TestConfiguration(AppSettings settings) : IStorageConfigurationStore
    {
        private AppSettings _settings = settings;
        public bool TryLoad(out AppSettings settings, out string? diagnostic)
        {
            settings = _settings;
            diagnostic = null;
            return true;
        }
        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class CountingLutLibrary(ILutLibrary inner) : ILutLibrary
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Folder, bool IncludeSubfolders)> Calls { get; } = [];
        public int Count(string folder) => _counts.GetValueOrDefault(folder);
        public Task<LutLibrarySnapshot> RefreshAsync(string folder, CancellationToken cancellationToken = default)
            => RefreshAsync(folder, false, cancellationToken);
        public Task<LutLibrarySnapshot> RefreshAsync(string folder, bool includeSubfolders,
            CancellationToken cancellationToken = default)
        {
            _counts[folder] = Count(folder) + 1;
            Calls.Add((folder, includeSubfolders));
            return inner.RefreshAsync(folder, includeSubfolders, cancellationToken);
        }
    }
}
