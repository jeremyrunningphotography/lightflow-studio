using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ColorManagementTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-color-{Guid.NewGuid():N}");
    private LightflowStorageCoordinator _storage = null!;
    private Guid _assetId;

    [Fact]
    public void Validation_RequiresSupportedCubeStructureAndExactDataCount()
    {
        var cube = CubeLutValidator.Validate(Cube(2, 0));
        Assert.True(cube.IsValid, cube.Diagnostic);
        Assert.Contains("1D", CubeLutValidator.Validate(OneDimensionalCube()).Diagnostic!);
        Assert.Contains("missing", CubeLutValidator.Validate("0 0 0\n"u8).Diagnostic!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("declares", CubeLutValidator.Validate("LUT_3D_SIZE 2\n0 0 0\n"u8).Diagnostic!, StringComparison.OrdinalIgnoreCase);
        Assert.False(CubeLutValidator.Validate([0xff, 0xfe]).IsValid);
    }

    [Fact]
    public async Task Library_PersistsContentIdentityAndMaterializesAfterSourceIsRemoved()
    {
        var source = WriteCube("Camera-Log.cube", 0);
        var imported = await _storage.ManagedLuts.ImportAsync(source);
        Assert.Equal(LutImportStatus.Imported, imported.Status);
        File.Delete(source);

        var firstPath = await _storage.ManagedLuts.MaterializeAsync(imported.Resource!.LutId);
        Assert.True(File.Exists(firstPath));
        await _storage.DisposeAsync();
        _storage = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;

        var restored = Assert.Single(await _storage.ManagedLuts.ListAsync());
        Assert.Equal(imported.Resource.LutId, restored.LutId);
        Assert.Equal(imported.Resource.ContentSha256, restored.ContentSha256);
        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(await _storage.ManagedLuts.MaterializeAsync(restored.LutId)));
    }

    [Fact]
    public async Task Library_AllowsDisplayNameCollisionsButDeduplicatesContent()
    {
        var first = await _storage.ManagedLuts.ImportAsync(WriteCube("Film-Look.cube", 0));
        var collision = await _storage.ManagedLuts.ImportAsync(WriteCube("Film_Look.cube", 1));
        var duplicate = await _storage.ManagedLuts.ImportAsync(WriteCube("Copy.cube", 0));

        Assert.Equal(LutImportStatus.Imported, first.Status);
        Assert.Equal(LutImportStatus.Imported, collision.Status);
        Assert.Equal(first.Resource!.DisplayName, collision.Resource!.DisplayName);
        Assert.NotEqual(first.Resource.LutId, collision.Resource.LutId);
        Assert.Equal(LutImportStatus.DuplicateContent, duplicate.Status);
        Assert.Equal(first.Resource.LutId, duplicate.Resource!.LutId);
        Assert.Equal(2, (await _storage.ManagedLuts.ListAsync()).Count);
    }

    [Fact]
    public async Task Library_RemovesUnassignedResourcesWithoutChangingImportedSource()
    {
        var source = WriteCube("Disposable.cube", 0);
        var resource = (await _storage.ManagedLuts.ImportAsync(source)).Resource!;

        var removed = await _storage.ManagedLuts.RemoveAsync(resource.LutId);

        Assert.Equal(LutRemovalStatus.Removed, removed.Status);
        Assert.Empty(await _storage.ManagedLuts.ListAsync());
        Assert.True(File.Exists(source));
        Assert.Equal(LutRemovalStatus.NotFound, (await _storage.ManagedLuts.RemoveAsync(resource.LutId)).Status);
    }

    [Fact]
    public async Task Rename_PreservesResourceAndColorIdentity()
    {
        var lut = (await _storage.ManagedLuts.ImportAsync(WriteCube("Technical.cube", 0))).Resource!;
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, lut.LutId);
        var before = await _storage.AssetColors.GetAsync(_assetId);

        var renamed = await _storage.ManagedLuts.RenameAsync(lut.LutId, "Camera transform");
        var after = await _storage.AssetColors.GetAsync(_assetId);

        Assert.Equal(lut.LutId, renamed!.LutId);
        Assert.Equal("Camera transform", after.Camera!.DisplayName);
        Assert.Equal(before.ColorIdentity, after.ColorIdentity);
    }

    [Fact]
    public async Task TypedAssignments_SupportEachOrderedStageAndIndependentClearing()
    {
        var camera = (await _storage.ManagedLuts.ImportAsync(WriteCube("Camera.cube", 0))).Resource!;
        var creative = (await _storage.ManagedLuts.ImportAsync(WriteCube("Creative.cube", 1))).Resource!;
        var original = await _storage.AssetColors.GetAsync(_assetId);

        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, camera.LutId);
        var cameraOnly = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal(camera.LutId, cameraOnly.Camera!.LutId);
        Assert.Null(cameraOnly.Creative);

        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Creative, creative.LutId);
        var both = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal([camera.LutId, creative.LutId], both.OrderedPipeline.Select(item => item.LutId));
        Assert.NotEqual(cameraOnly.ColorIdentity, both.ColorIdentity);

        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, null);
        var creativeOnly = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Null(creativeOnly.Camera);
        Assert.Equal(creative.LutId, creativeOnly.Creative!.LutId);

        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Creative, null);
        var cleared = await _storage.AssetColors.GetAsync(_assetId);
        Assert.False(cleared.HasColor);
        Assert.Equal(original.ColorIdentity, cleared.ColorIdentity);
    }

    [Fact]
    public async Task Assignments_PersistAcrossRestartAndRootRemapWithStableAssetIdentity()
    {
        var lut = (await _storage.ManagedLuts.ImportAsync(WriteCube("Camera.cube", 0))).Resource!;
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, lut.LutId);
        var root = Assert.Single(await _storage.MediaRoots.ListAsync());
        var moved = Directory.CreateDirectory(Path.Combine(_root, "moved-media")).FullName;
        Assert.True((await _storage.MediaRoots.RemapAsync(root.RootId, moved)).Succeeded);
        await _storage.DisposeAsync();
        _storage = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;

        var restored = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal(_assetId, restored.AssetId);
        Assert.Equal(lut.LutId, restored.Camera!.LutId);
    }

    [Fact]
    public async Task BulkAssignmentFailure_IsAtomic()
    {
        var lut = (await _storage.ManagedLuts.ImportAsync(WriteCube("Camera.cube", 0))).Resource!;
        var missingAsset = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _storage.AssetColors.SetAsync([
            new(_assetId, lut.LutId, null),
            new(missingAsset, lut.LutId, null)
        ]));

        Assert.False((await _storage.AssetColors.GetAsync(_assetId)).HasColor);
    }

    [Fact]
    public async Task AssignedLutCannotBeRemovedAndCorruptContentIsSurfaced()
    {
        var lut = (await _storage.ManagedLuts.ImportAsync(WriteCube("Camera.cube", 0))).Resource!;
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, lut.LutId);
        var blocked = await _storage.ManagedLuts.RemoveAsync(lut.LutId);
        Assert.Equal(LutRemovalStatus.Assigned, blocked.Status);

        Execute("UPDATE LutResources SET CubeContent=X'00' WHERE LutId=$id;", ("$id", lut.LutId.ToString("D")));
        var intent = await _storage.AssetColors.GetAsync(_assetId);
        Assert.Equal(LutResourceAvailability.Invalid, intent.Camera!.Availability);
        await Assert.ThrowsAsync<InvalidDataException>(() => _storage.ManagedLuts.MaterializeAsync(lut.LutId));
    }

    [Fact]
    public async Task ColorIdentity_IgnoresUnrelatedAssetMetadataButChangesWithEitherStage()
    {
        var camera = (await _storage.ManagedLuts.ImportAsync(WriteCube("Camera.cube", 0))).Resource!;
        var creative = (await _storage.ManagedLuts.ImportAsync(WriteCube("Creative.cube", 1))).Resource!;
        var original = await _storage.AssetColors.GetAsync(_assetId);
        Execute("UPDATE MediaAssets SET MediaType='image',UpdatedUtc=$now WHERE AssetId=$id;",
            ("$now", DateTime.UtcNow.ToString("O")), ("$id", _assetId.ToString("D")));
        Assert.Equal(original.ColorIdentity, (await _storage.AssetColors.GetAsync(_assetId)).ColorIdentity);

        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Camera, camera.LutId);
        var withCamera = await _storage.AssetColors.GetAsync(_assetId);
        await _storage.AssetColors.SetStageAsync([_assetId], ColorLutStage.Creative, creative.LutId);
        var withBoth = await _storage.AssetColors.GetAsync(_assetId);
        Assert.NotEqual(original.ColorIdentity, withCamera.ColorIdentity);
        Assert.NotEqual(withCamera.ColorIdentity, withBoth.ColorIdentity);
    }

    public async Task InitializeAsync()
    {
        _storage = (await LightflowStorageCoordinator.StartAsync(_root)).Coordinator!;
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

    private string WriteCube(string name, int offset)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Cube(2, offset));
        return path;
    }

    private static byte[] Cube(int size, int offset)
    {
        var rows = Enumerable.Range(0, size * size * size)
            .Select(index => $"{index + offset}.0 {index + offset}.1 {index + offset}.2");
        return System.Text.Encoding.UTF8.GetBytes($"TITLE \"Test\"\nLUT_3D_SIZE {size}\nDOMAIN_MIN 0 0 0\nDOMAIN_MAX 1 1 1\n{string.Join('\n', rows)}\n");
    }

    private static byte[] OneDimensionalCube() =>
        "LUT_1D_SIZE 2\n0 0 0\n1 1 1\n"u8.ToArray();

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new CatalogSqliteConnectionFactory(_storage.CatalogSession.DatabasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }
}
