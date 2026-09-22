using Xunit;

namespace LightflowStudio.Tests;

public sealed class PremiereReconciliationTests : IAsyncLifetime
{
    [Theory]
    [InlineData("40040000", "7924584", "10594584000", "201297096000")]
    [InlineData("14597916", "14597916", "10594584000", "370810440000")]
    [InlineData("40040000", "40040000", "10594584000", "1017080064000")]
    [InlineData("116366249", "116366249", "10594584000", "2955888936000")]
    [InlineData("10000001", "10000001", "10584000000", "254016025402")]
    [InlineData("11678333", "11678333", "8475667200", "296648352000")]
    public async Task FrameBoundaryReceiptValidatesSourceAndOriginTogether(string source, string relative, string frame, string expected)
    {
        var marker = new PremiereMarkerProjection(Guid.NewGuid(), _source.AssetId, 1, "", source, relative,
            source == relative ? null : Guid.NewGuid(), "target");
        Assert.Equal(expected, PremiereMarkerTiming.Project(marker, frame));
        var command = await _journal.PrepareMarkerAsync(_project, "root", _source, marker);
        var state = new PremiereMarkerState("guid", "target", "", expected, "0", "Comment", "", 3, ["guid"], frame);
        var receipt = new PremiereReceipt(command.Intent.OperationId, PremiereOutcome.Verified, "target", "verified",
            Verification: "point-marker-v2", MarkerState: state);
        await _journal.SaveReceiptAsync(command.Intent, receipt);
        foreach (var invalid in new string?[] { null, "", "0", "-1", "01", "9007199254740992" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.SaveReceiptAsync(command.Intent,
                receipt with { MarkerState = state with { FrameTicks = invalid } }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.SaveReceiptAsync(command.Intent,
            receipt with { MarkerState = state with { StartTicks = (long.Parse(expected) - 1).ToString() } }));
    }

    [Fact]
    public async Task MarkerCatalogPlanningAndIndependentTargetReceiptsSurviveRenameAndRepositoryRecreation()
    {
        var markers = new CatalogMarkerService(() => _session);
        var marker = (await markers.CreateAsync(_source.AssetId, TimeSpan.FromTicks(10000001))).Marker;
        var planned = Assert.Single(await _journal.PlanMarkersAsync(_source, "source-item"));
        Assert.Equal(marker.MarkerId, planned.MarkerId);
        var first = await _journal.PrepareMarkerAsync(_project, "root", _source, planned);
        var state = new PremiereMarkerState("marker-guid", "source-item", "", "254016025402", "0", "Comment", "", 3, ["marker-guid"], "10584000000");
        await _journal.MarkDispatchedAsync(first.Intent);
        await _journal.SaveReceiptAsync(first.Intent, new(first.Intent.OperationId, PremiereOutcome.Verified, "source-item", "Marker: verified",
            Verification: "point-marker-v2", MarkerState: state));
        await markers.RenameAsync(marker.MarkerId, marker.Revision, "Renamed");
        var newJournal = new CatalogPremiereHandoffs(() => _session);
        var renamed = Assert.Single(await newJournal.PlanMarkersAsync(_source, "source-item"));
        var retry = await newJournal.PrepareMarkerAsync(_project, "root", _source, renamed);
        Assert.Equal(first.Intent.OperationId, retry.Intent.OperationId);
        Assert.Equal(2, retry.Intent.Marker!.Revision);
        Assert.Equal("Renamed", retry.Intent.Marker.Name);
        Assert.Equal("marker-guid", retry.PreviousReceipt!.MarkerState!.Guid);
        Assert.Equal("", retry.PreviousReceipt.MarkerState.Name);
        foreach (var subclipId in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            var subclip = await newJournal.PrepareMarkerAsync(_project, "root", _source,
                renamed with { SubclipId = subclipId, PositionTicks = "1", TargetItemId = subclipId.ToString() });
            Assert.NotEqual(first.Intent.OperationId, subclip.Intent.OperationId);
        }
        var destination = await newJournal.PrepareMarkerAsync(_project with { Path = @"C:\disposable\other.prproj" }, "root", _source, renamed);
        Assert.NotEqual(first.Intent.OperationId, destination.Intent.OperationId);
        Assert.Equal(4, (await newJournal.ListAsync()).Count);
        await newJournal.SaveReceiptAsync(retry.Intent, new(retry.Intent.OperationId, PremiereOutcome.UnknownOutcome, null, "Marker: lost connection"));
        var lost = await newJournal.PrepareMarkerAsync(_project, "root", _source, renamed);
        Assert.Equal("marker-guid", lost.PreviousReceipt!.MarkerState!.Guid);
        Assert.Equal("", lost.PreviousReceipt.MarkerState.Name);
    }

    [Fact]
    public async Task MarkerReceiptRequiresExactPropertiesAndTemporarySourceIsRejected()
    {
        var markers = new CatalogMarkerService(() => _session);
        await markers.CreateAsync(_source.AssetId, TimeSpan.FromTicks(123));
        var marker = Assert.Single(await _journal.PlanMarkersAsync(_source, "source"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.PrepareMarkerAsync(_project, "root", _source with { IsSubclipPrerequisite = true }, marker));
        var command = await _journal.PrepareMarkerAsync(_project, "root", _source, marker);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.SaveReceiptAsync(command.Intent,
            new(command.Intent.OperationId, PremiereOutcome.Verified, "source", "unproven")));
        var state = new PremiereMarkerState("guid", "source", "", "3124398", "0", "Comment", "", 3, ["guid"], "10584000000");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.SaveReceiptAsync(command.Intent,
            new(command.Intent.OperationId, PremiereOutcome.Verified, "source", "wrong timing", Verification: "point-marker-v2", MarkerState: state)));
    }

    [Fact]
    public async Task Version16CatalogMigratesWithExistingMarkersAndNoProjectionState()
    {
        var locations = LightflowStorageLocations.Create(Path.Combine(_temp, "v16"));
        var prior = (await new CatalogDatabaseService(locations, null, CatalogMigrations.All.Take(16).ToArray()).CreateNewAsync()).Session!;
        var identity = prior.Identity.CatalogId;
        var roots = new MediaRootService(() => prior, new Machine(), new MediaRootFileSystem());
        var root = (await roots.CreateAsync("Old Catalog", Path.GetDirectoryName(_source.Path)!)).Root!;
        var assets = new MediaAssetService(new CatalogMediaAssetRepository(() => prior), roots, new SampledSourceFingerprintService());
        var asset = (await assets.CreateAsync(root.RootId, "source.mov", "video")).Asset!.Asset;
        var marker = (await new CatalogMarkerService(() => prior).CreateAsync(asset.AssetId, TimeSpan.FromTicks(120000001))).Marker;
        await prior.DisposeAsync();
        var recovery = new SqliteCatalogRecoveryService(locations);
        var opened = await new CatalogDatabaseService(locations, recovery).OpenExistingAsync();
        Assert.True(opened.IsSuccess);
        await using var migrated = opened.Session!;
        Assert.Equal(CatalogMigrations.All[^1].Version, migrated.SchemaVersion); Assert.Equal(identity, migrated.Identity.CatalogId);
        Assert.Equal(marker, Assert.Single(await new CatalogMarkerService(() => migrated).ListAsync(asset.AssetId)));
        Assert.Empty(await new CatalogPremiereHandoffs(() => migrated).ListAsync());
        Assert.Single(recovery.ListBackups(), b => b.Kind == CatalogBackupKind.Migration);
    }
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

    [Fact]
    public async Task SubclipMappingsPersistPerDestinationIdentityAndKeepIndependentReceipts()
    {
        var range = new PremiereRangeProjection("10000001", "30000002", "60000000");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = await _journal.PrepareSubclipAsync(_project, "root", null, _source,
            new(firstId, "First", 1, range, "source-item"));
        var second = await _journal.PrepareSubclipAsync(_project, "root", null, _source,
            new(secondId, "Second", 1, range, "source-item"));
        Assert.NotEqual(first.Intent.OperationId, second.Intent.OperationId);
        await _journal.MarkDispatchedAsync(first.Intent);
        await _journal.SaveReceiptAsync(first.Intent, new(first.Intent.OperationId, PremiereOutcome.Verified,
            "native-first", "created", PremiereProtocol.SubclipProjectionKey(first.Intent.Subclip!), "native-subclip-v4"));
        await _journal.SaveReceiptAsync(first.Intent, new(first.Intent.OperationId, PremiereOutcome.Failed,
            null, "transient failure"));

        var retry = await _journal.PrepareSubclipAsync(_project, "other-bin", null, _source,
            new(firstId, "First renamed", 2, range, "source-item"));
        Assert.Equal(first.Intent.OperationId, retry.Intent.OperationId);
        Assert.True(retry.PreviouslyDispatched);
        Assert.Equal("native-first", retry.PreviousReceipt!.ItemId);
        Assert.Equal(PremiereProtocol.SubclipProjectionKey(first.Intent.Subclip!), retry.PreviousReceipt.ProjectionKey);
        Assert.Equal("First renamed", retry.Intent.Subclip!.Name);
        Assert.Equal(2, (await _journal.ListAsync()).Count);
    }

    [Fact]
    public async Task VerifiedSubclipReceiptRequiresExactProjectionProof()
    {
        var projection = new PremiereSubclipProjection(Guid.NewGuid(), "Proof", 2,
            new("10", "90", "100"), "source-item");
        var command = await _journal.PrepareSubclipAsync(_project, "root", null, _source, projection);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.SaveReceiptAsync(command.Intent,
            new(command.Intent.OperationId, PremiereOutcome.Verified, "source-item", "source-shaped receipt")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.SaveReceiptAsync(command.Intent,
            new(command.Intent.OperationId, PremiereOutcome.Verified, "native-item", "wrong projection", "wrong")));
        var receipt = new PremiereReceipt(command.Intent.OperationId, PremiereOutcome.Verified, "native-item", "created",
            PremiereProtocol.SubclipProjectionKey(projection), "native-subclip-v4");
        await _journal.SaveReceiptAsync(command.Intent, receipt);
        Assert.Equal(receipt, (await _journal.PrepareSubclipAsync(_project, "root", null, _source, projection)).PreviousReceipt);
    }

    [Fact]
    public void SubclipProjectionProofMatchesJavaScriptJsonForUnicodeNames()
    {
        var projection = new PremiereSubclipProjection(Guid.NewGuid(), "Café 二", 2,
            new("10", "90", "100"), "source-item");

        Assert.Equal("{\"name\":\"Café 二\",\"revision\":2,\"range\":\"10:90:100\",\"hardBoundaries\":true,\"takeVideo\":true,\"takeAudio\":true}",
            PremiereProtocol.SubclipProjectionKey(projection));
    }

    [Fact]
    public void RangeProjectionPreservesExclusiveOutWithNearestPremiereTickAtNtscAndVfrPositions()
    {
        var frame = TimeSpan.FromTicks(333667); // nearest Lightflow tick to 1001/30000 second
        var vfrPosition = TimeSpan.FromTicks(12_345_679);
        Assert.True(PremiereRangeProjection.TryCreate(new(TimeSpan.FromSeconds(10), frame,
            frame + vfrPosition), out var projection));
        Assert.Equal(frame.Ticks.ToString(), projection!.InTicks);
        Assert.Equal((frame + vfrPosition).Ticks.ToString(), projection.OutTicks);
        Assert.True(projection.IsValid());
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
        var destination = receipt with { Message = "The mapped native Subclip is outside the selected destination." };
        Assert.Contains("Move it back", PremiereJob.UserMessage(destination));
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

    [Fact]
    public void SubclipJobsExposeTypedItemStatusesWithoutRepeatingTheLastItemAsAnIssue()
    {
        var sent = new PremiereReceipt(Guid.NewGuid(), PremiereOutcome.Verified, "sent-item", "created");
        var conflict = new PremiereReceipt(Guid.NewGuid(), PremiereOutcome.Conflict, "conflict-item",
            "The mapped native Subclip is outside the selected destination.");
        var planned = new[]
        {
            new PremierePlannedSubclip(_source, new(Guid.NewGuid(), "Sent moment", 1, new("10", "40", "100"), "source")),
            new PremierePlannedSubclip(_source, new(Guid.NewGuid(), "Conflicting moment", 1, new("50", "90", "100"), "source"))
        };
        var items = new[]
        {
            new PremiereJobItem($"subclip:{planned[0].Projection.SubclipId:D}", "Sent moment", PremiereJobItemState.Sent, sent),
            new PremiereJobItem($"subclip:{planned[1].Projection.SubclipId:D}", "Conflicting moment", PremiereJobItemState.Conflict, conflict)
        };
        var job = new PremiereJob(Guid.NewGuid(), _project, [_source], JobState.CompletedWithWarnings, 2,
            [sent, conflict], "Sent moment — Sent", DateTimeOffset.UtcNow, planned, items);

        var card = job.Card(expanded: true);
        var details = Assert.IsType<PremiereJobDetailsPresentation>(card.Details);
        Assert.Collection(details.Items,
            item => { Assert.Equal("Sent", item.Status); Assert.Equal(PremiereJobItemState.Sent, item.State); Assert.Empty(item.Detail); },
            item => { Assert.Equal("Conflict", item.Status); Assert.Equal(PremiereJobItemState.Conflict, item.State); Assert.Contains("Move it back", item.Detail); });
        Assert.Equal("1 item needs attention.", card.Issue);
        Assert.DoesNotContain("Sent moment — Sent", card.Issue);
        Assert.Equal(card.Issue, job.WorkspaceItem().Issue);
    }

    public async Task DisposeAsync()
    {
        if (_session is not null) await _session.DisposeAsync();
        try { Directory.Delete(_temp, true); } catch (IOException) { }
    }
    private sealed class Machine : IMachineIdentityProvider { public string GetMachineId() => "test-machine"; }
}
