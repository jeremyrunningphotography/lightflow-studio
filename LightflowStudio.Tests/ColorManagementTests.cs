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
        Assert.Equal(path, await _storage.Luts.ResolvePathAsync(restored.LutId, _luts));
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
        var resource = Assert.Single((await _storage.Luts.RefreshAsync(_luts)).Resources);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, resource.LutId);
        var before = await _storage.AssetColors.GetAsync(_assetId);
        var renamed = Path.Combine(_luts, "Camera Transform.cube");
        File.Move(original, renamed);

        var refreshed = Assert.Single((await _storage.Luts.RefreshAsync(_luts)).Resources);
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
        var original = Assert.Single((await _storage.Luts.RefreshAsync(_luts)).Resources);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, original.LutId);

        File.Delete(path);
        var removed = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal(LutResourceAvailability.Missing, removed.Camera!.Availability);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.Luts.ResolvePathAsync(original.LutId, _luts));

        WriteCube(_luts, "Technical.cube", 1);
        var replacement = Assert.Single((await _storage.Luts.RefreshAsync(_luts)).Resources);
        var changed = await _storage.AssetColors.GetAsync(_assetId);
        Assert.NotEqual(original.LutId, replacement.LutId);
        Assert.Equal(original.LutId, changed.Camera!.LutId);
        Assert.Equal(LutResourceAvailability.Missing, changed.Camera.Availability);
    }

    [Fact]
    public async Task FolderChangePreservesAssignmentOnlyWhenSameContentExists()
    {
        WriteCube(_luts, "Original Name.cube", 0);
        var resource = Assert.Single((await _storage.Luts.RefreshAsync(_luts)).Resources);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, resource.LutId);
        var other = Directory.CreateDirectory(Path.Combine(_root, "other-luts")).FullName;
        SetLutFolder(other);
        Assert.Equal(LutResourceAvailability.Missing, (await _storage.AssetColors.GetAsync(_assetId)).Camera!.Availability);

        WriteCube(other, "Different Name.cube", 0);
        var matched = Assert.Single((await _storage.Luts.RefreshAsync(other)).Resources);
        var restored = await _storage.AssetColors.GetAsync(_assetId);

        Assert.Equal(resource.LutId, matched.LutId);
        Assert.Equal(LutResourceAvailability.Available, restored.Camera!.Availability);
        Assert.Equal("Different Name", restored.Camera.DisplayName);
    }

    [Fact]
    public async Task TypedAssignmentsSupportOrderedStagesIndependentClearingAndStableAssetIdentity()
    {
        WriteCube(_luts, "Camera.cube", 0);
        WriteCube(_luts, "Creative.cube", 1);
        var resources = (await _storage.Luts.RefreshAsync(_luts)).Resources;
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
    public async Task BulkAssignmentFailureIsAtomicAndColorIdentityIgnoresUnrelatedMetadata()
    {
        WriteCube(_luts, "Camera.cube", 0);
        var lut = Assert.Single((await _storage.Luts.RefreshAsync(_luts)).Resources);
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
    }

    private void SetLutFolder(string folder)
    {
        var settings = _storage.Settings with { LutFolder = folder };
        _storage.SaveSettings(settings);
    }

    private static string WriteCube(string folder, string name, int offset)
    {
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
}
