using System.Text.Json;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class PathIdentityPersistenceTests : IAsyncLifetime
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "lf009-identity-" + Guid.NewGuid().ToString("N"));
    private LightflowStorageCoordinator _storage = null!;
    private MediaRootInfo _root = null!;
    private Guid _asset;
    private string _source = null!;

    public async Task InitializeAsync()
    {
        _source = Directory.CreateDirectory(Path.Combine(_workspace, "source")).FullName;
        File.WriteAllText(Path.Combine(_source, "café.mp4"), "fixture");
        _storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_workspace, "profile"))).Coordinator!;
        _root = (await _storage.MediaRoots.CreateAsync("Originals", _source)).Root!;
        _asset = (await _storage.MediaAssets.CreateAsync(_root.RootId, "café.mp4", "video")).Asset!.Asset.AssetId;
        using var connection = _storage.CatalogSession.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MediaAssetDescriptions(AssetId,Notes,Revision,CreatedUtc,UpdatedUtc)
            VALUES($asset,'authored fixture note',7,$now,$now);
            INSERT INTO MediaAssetClassifications(AssetId,Rating,Flag,ColorLabel,Revision,CreatedUtc,UpdatedUtc)
            VALUES($asset,5,1,3,4,$now,$now);
            INSERT INTO MediaAssetKeywords(AssetId,Keyword,Ordinal,CreatedUtc) VALUES($asset,'precious',0,$now);
            INSERT INTO Collections(CollectionId,Name,Ordinal,Revision,CreatedUtc,UpdatedUtc)
            VALUES($collection,'Fixture picks',0,3,$now,$now);
            INSERT INTO CollectionAssets(CollectionId,AssetId,Ordinal,Revision,CreatedUtc,UpdatedUtc)
            VALUES($collection,$asset,0,2,$now,$now);
            INSERT INTO TimelineMarkers(MarkerId,AssetId,PositionTicks,Name,Revision,CreatedUtc,UpdatedUtc)
            VALUES($marker,$asset,100,'Authored marker',2,$now,$now);
            INSERT INTO Subclips(SubclipId,AssetId,Name,Ordinal,InTicks,OutTicks,SourceDurationTicks,Revision,CreatedUtc,UpdatedUtc)
            VALUES($subclip,$asset,'Authored range',0,100,200,300,2,$now,$now);
            """;
        command.Parameters.AddWithValue("$asset", _asset.ToString("D"));
        command.Parameters.AddWithValue("$collection", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$marker", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$subclip", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task UnicodeCollisionInsertPreservesEveryCatalogRow()
    {
        var before = Snapshot();
        File.WriteAllText(Path.Combine(_source, "cafe\u0301.mp4"), "distinct fixture");
        var result = await _storage.MediaAssets.CreateAsync(_root.RootId, "cafe\u0301.mp4", "video");
        Assert.False(result.Succeeded);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task ReconciliationCollisionRefusesBeforeObservationsOrMissingInference()
    {
        var before = Snapshot();
        File.WriteAllText(Path.Combine(_source, "cafe\u0301.mp4"), "distinct fixture");
        var result = await _storage.CatalogReconciliation.ReconcileAsync(new(_root.RootId));
        Assert.False(result.Succeeded);
        Assert.Empty(result.Items);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task NativeCaseOnlyRenameRefusesWithoutChangingAuthoredState()
    {
        var before = Snapshot();
        var intermediate = Path.Combine(_source, "rename-temporary.mp4");
        File.Move(Path.Combine(_source, "café.mp4"), intermediate);
        File.Move(intermediate, Path.Combine(_source, "CAFÉ.mp4"));
        var result = await _storage.CatalogReconciliation.ReconcileAsync(new(_root.RootId));
        Assert.False(result.Succeeded);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task NativeHardLinkRefusesObservationWithoutChangingAnyRows()
    {
        var file = Path.Combine(_source, "café.mp4");
        var target = Path.Combine(_workspace, "hardlink-target.mp4");
        File.Move(file, target);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        { Arguments = $"/c mklink /H \"{file}\" \"{target}\"", UseShellExecute = false, CreateNoWindow = true });
        await process!.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        var before = Snapshot();
        Assert.False((await _storage.MediaAssets.ObserveAsync(_asset)).Succeeded);
        Assert.Equal(before, Snapshot());
        Assert.Equal("fixture", File.ReadAllText(target));
    }

    [Fact]
    public async Task NativeSkippedLinkCannotBeInferredAsMissing()
    {
        var child = Directory.CreateDirectory(Path.Combine(_source, "linked")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_workspace, "outside")).FullName;
        Directory.Delete(child);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        { Arguments = $"/c mklink /J \"{child}\" \"{outside}\"", UseShellExecute = false, CreateNoWindow = true });
        await process!.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        var before = Snapshot();
        var result = await _storage.CatalogReconciliation.ReconcileAsync(new(_root.RootId));
        Assert.False(result.Succeeded); Assert.Empty(result.Items);
        Assert.Equal(before, Snapshot());
        Directory.Delete(child);
    }

    [Fact]
    public async Task RemapRejectsHistoricalUnsupportedNameWithoutChangingAnyRows()
    {
        using (var connection = _storage.CatalogSession.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE MediaAssets SET RelativePath='CON.mp4',RelativePathKey='CON.MP4' WHERE AssetId=$asset;";
            command.Parameters.AddWithValue("$asset", _asset.ToString("D")); command.ExecuteNonQuery();
        }
        var destination = Directory.CreateDirectory(Path.Combine(_workspace, "destination")).FullName;
        var before = Snapshot();
        Assert.False((await _storage.MediaRoots.RemapAsync(_root.RootId, destination)).Succeeded);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task SafeRemapAndReopenPreserveCatalogRootAssetAndAuthoredState()
    {
        var id = _storage.CatalogSession.Identity.CatalogId;
        var destination = Directory.CreateDirectory(Path.Combine(_workspace, "destination")).FullName;
        File.Copy(Path.Combine(_source, "café.mp4"), Path.Combine(destination, "café.mp4"));
        Assert.True((await _storage.MediaRoots.RemapAsync(_root.RootId, destination)).Succeeded);
        var before = Snapshot();
        await _storage.DisposeAsync();
        _storage = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_workspace, "profile"))).Coordinator!;
        Assert.Equal(id, _storage.CatalogSession.Identity.CatalogId);
        Assert.Equal(before, Snapshot());
        var row = Assert.Single(await _storage.MediaAssets.ListAsync());
        Assert.Equal(_asset, row.AssetId); Assert.Equal(_root.RootId, row.RootId);
        Assert.Equal("café.mp4", row.RelativePath); Assert.Equal("CAFÉ.MP4", row.RelativePathKey);
    }

    [Fact]
    public async Task MissingMediaAndCancelledRemapPreserveIdentities()
    {
        var destination = Directory.CreateDirectory(Path.Combine(_workspace, "missing-media")).FullName;
        var before = Snapshot();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _storage.MediaRoots.RemapAsync(
            _root.RootId, destination, new CancellationToken(true)));
        Assert.Equal(before, Snapshot());
        Assert.True((await _storage.MediaRoots.RemapAsync(_root.RootId, destination)).Succeeded);
        var row = Assert.Single(await _storage.MediaAssets.ListAsync());
        Assert.Equal(_asset, row.AssetId); Assert.Equal(_root.RootId, row.RootId);
        Assert.Equal(MediaAssetSourceStatus.Available, row.SourceStatus); // A remap itself does not infer missing state.
    }

    [Fact]
    public async Task NativeJunctionCannotCreateSecondLogicalRootForSameFolder()
    {
        var alias = Path.Combine(_workspace, "alias");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        { Arguments = $"/c mklink /J \"{alias}\" \"{_source}\"", UseShellExecute = false, CreateNoWindow = true });
        await process!.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        var before = Snapshot();
        Assert.False((await _storage.MediaRoots.CreateAsync("Alias", alias)).Succeeded);
        Assert.Equal(before, Snapshot());
        Directory.Delete(alias);
    }

    [Fact]
    public async Task NativeLinkedChildRefusalDoesNotMarkAuthoredAssetMissing()
    {
        var child = Directory.CreateDirectory(Path.Combine(_source, "child")).FullName;
        File.WriteAllText(Path.Combine(child, "clip.mp4"), "fixture");
        var created = await _storage.MediaAssets.CreateAsync(_root.RootId, "child/clip.mp4", "video");
        Assert.True(created.Succeeded, created.Diagnostic);
        var outside = Path.Combine(_workspace, "outside"); Directory.Move(child, outside);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        { Arguments = $"/c mklink /J \"{child}\" \"{outside}\"", UseShellExecute = false, CreateNoWindow = true });
        await process!.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        var before = Snapshot();
        Assert.False((await _storage.MediaAssets.ObserveAsync(created.Asset!.Asset.AssetId)).Succeeded);
        Assert.Equal(before, Snapshot());
        Directory.Delete(child);
    }

    private string Snapshot()
    {
        using var connection = _storage.CatalogSession.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var tables = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
        var values = new List<string>();
        foreach (var table in tables)
        {
            command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                values.Add(table + JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount)
                    .Select(i => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i))).ToArray()));
        }
        return string.Join('\n', values);
    }

    public async Task DisposeAsync()
    {
        await _storage.DisposeAsync();
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true);
    }
}
