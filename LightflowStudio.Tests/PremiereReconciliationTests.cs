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

    [Fact]
    public void SendReadinessSeparatesHealthyConnectionFromAnActionableProject()
    {
        var disconnected = PremiereSendState.Present(new(PremiereConnectionState.ConnectionProblem, "Reconnect."));
        Assert.Equal(PremiereSendReadiness.Disconnected, disconnected.Readiness);
        var noProject = PremiereSendState.Present(new(PremiereConnectionState.Connected, "Connected.", new("s", "1.0.7", 1, "26.5", "9", null, [])));
        Assert.Equal(PremiereSendReadiness.ProjectRequired, noProject.Readiness);
        Assert.True(noProject.IsProjectMissing);
        Assert.Equal("Open or create a Premiere project, then click Refresh.", noProject.Guidance);
        var active = PremiereSendState.Present(new(PremiereConnectionState.Connected, "Connected.", new("s", "1.0.7", 1, "26.5", "9", new("p", @"C:\edit.prproj", "edit"), [new("root", "Root")] )));
        Assert.Equal(PremiereSendReadiness.DestinationRequired, active.Readiness);
        Assert.True(active.IsActionable);
    }

    [Fact]
    public void RefreshClearsClosedOrSwitchedProjectAndAcceptsTheNewProject()
    {
        var state = new PremiereSendState();
        var first = new PremiereProject("first", @"C:\first.prproj", "first");
        state.Refresh(new(PremiereConnectionState.Connected, "", new("s", "1.0.7", 1, "26.5", "9", first, [new("first-root", "Root")])));
        state.SelectedBinId = "first-root";
        Assert.NotNull(state.DestinationId);
        state.Refresh(new(PremiereConnectionState.Connected, "", new("s", "1.0.7", 1, "26.5", "9", null, [])));
        Assert.Null(state.DestinationId);
        Assert.Null(state.SelectedBinId);
        var second = new PremiereProject("second", @"C:\second.prproj", "second");
        state.Refresh(new(PremiereConnectionState.Connected, "", new("s", "1.0.7", 1, "26.5", "9", second, [new("second-root", "Root")])));
        Assert.Equal(PremiereProtocol.DestinationId(second), state.DestinationId);
        Assert.Single(state.Bins);
    }

    [Fact]
    public void JobsPresentConcreteRecoveryInsteadOfInternalConflictDiagnostics()
    {
        var receipt = new PremiereReceipt(Guid.NewGuid(), PremiereOutcome.Conflict, null, "Operation payload changed; no mutation performed.");
        Assert.Equal("Not sent", PremiereJob.OutcomeText(receipt));
        Assert.DoesNotContain("payload", PremiereJob.UserMessage(receipt), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Resolve", PremiereJob.UserMessage(receipt), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunningMultiSourceHandoffPublishesSendingProgressAfterEachTerminalReceipt()
    {
        var sources = new[]
        {
            new PremiereSource(Guid.NewGuid(), "one.mov", "1", "1"),
            new PremiereSource(Guid.NewGuid(), "two.mov", "1", "1")
        };
        var job = new PremiereJob(Guid.NewGuid(), _project, sources, JobState.Running, 1, [], "one.mov: Imported.", DateTimeOffset.UtcNow);

        Assert.Equal(50, job.Progress);
        Assert.Equal("Sending", job.Card(expanded: false).State);
        Assert.Equal(50, job.Card(expanded: false).Progress);
        Assert.Equal(50, job.WorkspaceItem().Progress);
    }

    public async Task DisposeAsync()
    {
        if (_session is not null) await _session.DisposeAsync();
        try { Directory.Delete(_temp, true); } catch (IOException) { }
    }
    private sealed class Machine : IMachineIdentityProvider { public string GetMachineId() => "test-machine"; }
}
