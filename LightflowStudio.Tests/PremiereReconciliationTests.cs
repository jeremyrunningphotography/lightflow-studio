using Xunit;

namespace LightflowStudio.Tests;

public sealed class PremiereReconciliationTests : IAsyncLifetime
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "Lightflow-Premiere-reconciliation", Guid.NewGuid().ToString("N"));
    private CatalogDatabaseSession _session = null!;
    private CatalogPremiereHandoffs _journal = null!;
    private PremiereSource _source = null!;
    private Guid _rootId;
    private MediaAssetService _assets = null!;
    private readonly PremiereProject _project = new("project-guid", @"C:\disposable\edit.prproj", "edit.prproj");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_temp);
        _session = (await new CatalogDatabaseService(LightflowStorageLocations.Create(_temp)).CreateNewAsync()).Session!;
        var rootPath = Directory.CreateDirectory(Path.Combine(_temp, "media")).FullName;
        var path = Path.Combine(rootPath, "source.mov");
        await File.WriteAllTextAsync(path, "synthetic test media");
        var roots = new MediaRootService(() => _session, new Machine(), new MediaRootFileSystem());
        _rootId = (await roots.CreateAsync("Test", rootPath)).Root!.RootId;
        _assets = new MediaAssetService(new CatalogMediaAssetRepository(() => _session), roots, new SampledSourceFingerprintService());
        var asset = (await _assets.CreateAsync(_rootId, "source.mov", "video")).Asset!.Asset;
        _source = new(asset.AssetId, path, asset.FileSizeBytes.ToString(), asset.LastWriteUtcTicks.ToString());
        _journal = new(() => _session);
    }

    [Fact]
    public async Task FourSourcePartialRetriesKeepAssetIdentityAndUpdateMutableProjection()
    {
        var sources = new List<PremiereSource> { _source };
        for (var index = 1; index < 4; index++)
        {
            var relative = $"source-{index}.mov";
            var path = Path.Combine(Path.GetDirectoryName(_source.Path)!, relative);
            await File.WriteAllTextAsync(path, "synthetic test media " + index);
            var asset = (await _assets.CreateAsync(_rootId, relative, "video")).Asset!.Asset;
            sources.Add(new(asset.AssetId, path, asset.FileSizeBytes.ToString(), asset.LastWriteUtcTicks.ToString()));
        }
        foreach (var source in sources)
        {
            var first = await _journal.PrepareAsync(_project, "root", "first", source with { Range = new("10", "90", "100") });
            await _journal.MarkDispatchedAsync(first.Intent);
            var noRangeRetry = await _journal.PrepareAsync(_project, "other-bin", null, source);
            Assert.Equal(first.Intent.OperationId, noRangeRetry.Intent.OperationId);
            Assert.Equal("other-bin", noRangeRetry.Intent.BinId);
            Assert.Null(noRangeRetry.Intent.Source.Range);
            var rangedRetry = await _journal.PrepareAsync(_project, "root", null, source with { Range = new("20", "80", "100") });
            Assert.Equal(first.Intent.OperationId, rangedRetry.Intent.OperationId);
            Assert.Equal("20", rangedRetry.Intent.Source.Range!.InTicks);
        }
        Assert.Equal(4, (await _journal.ListAsync()).Count);
    }

    [Theory]
    [InlineData(0, 0, 120, true)]
    [InlineData(100, 0, 120, true)]
    [InlineData(100, 50, 120, false)]
    [InlineData(100, 100, -120, true)]
    [InlineData(100, 50, -120, false)]
    public void NestedSendMediaWheelTransfersOnlyAtDirectionalBoundaries(double height, double offset, int delta, bool transfers)
        => Assert.Equal(transfers, PremiereSendWindow.ShouldTransferWheelToDialog(height, offset, delta));

    public async Task DisposeAsync()
    {
        if (_session is not null) await _session.DisposeAsync();
        try { Directory.Delete(_temp, true); } catch (IOException) { }
    }
    private sealed class Machine : IMachineIdentityProvider { public string GetMachineId() => "test-machine"; }
}
