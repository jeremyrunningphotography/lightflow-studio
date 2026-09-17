using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

[CollectionDefinition("Premiere bridge", DisableParallelization = true)]
public class PremiereBridgeCollection;

[Collection("Premiere bridge")]
public sealed class PremiereBridgeTests : IAsyncLifetime
{
    [Fact]
    public async Task AutomaticRenewalKeepsValidCredentialsAndRejectsOldCredentialsAfterExpiry()
    {
        await _bridge.RenewExpiredPairingAsync();
        Assert.True(_bridge.Authenticate("localhost:47857", _authorization, false, IPAddress.Loopback));
        _now = _now.AddHours(8);
        Assert.False(_bridge.Authenticate("localhost:47857", _authorization, false, IPAddress.Loopback));
        await _bridge.RenewExpiredPairingAsync();
        using var renewed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_bridge.PairingDirectory, "lightflow-pairing.json")));
        var authorization = "Bearer " + renewed.RootElement.GetProperty("token").GetString();
        Assert.NotEqual(_authorization, authorization);
        Assert.False(_bridge.Authenticate("localhost:47857", _authorization, false, IPAddress.Loopback));
        Assert.True(_bridge.Authenticate("localhost:47857", authorization, false, IPAddress.Loopback));
        Assert.False(_bridge.Authenticate("localhost:47857", authorization, true, IPAddress.Loopback));
        Assert.False(_bridge.Authenticate("localhost:47857", authorization, false, IPAddress.Parse("192.0.2.1")));
        Assert.Equal(PremiereConnectionState.Ready, _bridge.Connection.State);
    }
    [Fact]
    public async Task FailedExpiredCredentialPublicationCanBeRetried()
    {
        _now = _now.AddHours(8);
        var path = Path.Combine(_bridge.PairingDirectory, "lightflow-pairing.json");
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var failure = await Record.ExceptionAsync(() => _bridge.RenewExpiredPairingAsync());
            Assert.True(failure is IOException or UnauthorizedAccessException);
        }
        await _bridge.RenewExpiredPairingAsync();
        using var renewed = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.True(_bridge.Authenticate("localhost:47857", "Bearer " + renewed.RootElement.GetProperty("token").GetString(), false, IPAddress.Loopback));
    }
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "Lightflow-Premiere-tests", Guid.NewGuid().ToString("N"));
    private CatalogDatabaseSession _session = null!;
    private CatalogPremiereHandoffs _journal = null!;
    private PremiereBridge _bridge = null!;
    private HttpClient _client = null!;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private PremiereSource _source = null!;
    private MediaAssetService _assets = null!;
    private Guid _rootId;
    private readonly PremiereProject _project = new("project-guid", @"C:\disposable\edit.prproj", "edit.prproj");
    private string _authorization = "";
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_temp);
        var locations = LightflowStorageLocations.Create(_temp);
        _session = (await new CatalogDatabaseService(locations).CreateNewAsync()).Session!;
        var roots = new MediaRootService(() => _session, new Machine(), new MediaRootFileSystem());
        var rootPath = Directory.CreateDirectory(Path.Combine(_temp, "media")).FullName;
        var path = Path.Combine(rootPath, "source.mov");
        await File.WriteAllTextAsync(path, "synthetic test media");
        var root = (await roots.CreateAsync("Test", rootPath)).Root!;
        _rootId = root.RootId;
        _assets = new MediaAssetService(new CatalogMediaAssetRepository(() => _session), roots, new SampledSourceFingerprintService());
        var asset = (await _assets.CreateAsync(root.RootId, "source.mov", "video")).Asset!.Asset;
        _source = new(asset.AssetId, path, asset.FileSizeBytes.ToString(), asset.LastWriteUtcTicks.ToString());
        _journal = new(() => _session);
        _bridge = new(_journal, Path.Combine(_temp, "pairing"), () => _now);
        await _bridge.StartAsync();
        using var pairing = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_bridge.PairingDirectory, "lightflow-pairing.json")));
        _authorization = "Bearer " + pairing.RootElement.GetProperty("token").GetString();
        _client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new(PremiereProtocol.Endpoint) };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", _authorization);
        _client.DefaultRequestHeaders.Add("X-Lightflow-Session", "session-1");
    }
    private sealed class Machine : IMachineIdentityProvider { public string GetMachineId() => "test-machine"; }
    private PremiereHello Hello => new("session-1", PremiereProtocol.CompanionVersion, 1, "26.5.0", "9.3.0", _project, [new("root", "Root")]);
    private async Task<HttpResponseMessage> Post(string path, object value)
    {
        await Task.Delay(70);
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(JsonSerializer.Serialize(value, PremiereProtocol.Json), Encoding.UTF8) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await _client.SendAsync(request);
    }
    private async Task<HttpResponseMessage> PostReceipt(PremiereCommand command, PremiereReceipt receipt)
    {
        await Task.Delay(70);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/receipt")
        {
            Content = new StringContent(JsonSerializer.Serialize(receipt, PremiereProtocol.Json), Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Lightflow-Dispatch", command.DispatchId.ToString());
        return await _client.SendAsync(request);
    }
    private async Task<PremiereCommand> PollCommandAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var response = await Post("/v1/poll", new { });
            if (response.StatusCode == HttpStatusCode.OK)
                return (await response.Content.ReadFromJsonAsync<PremiereCommand>(PremiereProtocol.Json))!;
        }
        throw new TimeoutException("No Premiere command was dispatched.");
    }
    private async Task<PremiereSource> CreateSourceAsync(string name)
    {
        var path = Path.Combine(Path.GetDirectoryName(_source.Path)!, name);
        await File.WriteAllTextAsync(path, "synthetic test media " + name);
        var asset = (await _assets.CreateAsync(_rootId, name, "video")).Asset!.Asset;
        return new(asset.AssetId, path, asset.FileSizeBytes.ToString(), asset.LastWriteUtcTicks.ToString());
    }
    [Fact]
    public async Task InstalledOrStartedIsNotConnected_HeartbeatExpiresAndCompatibilityRejects()
    {
        Assert.Equal(PremiereConnectionState.Ready, _bridge.Connection.State);
        Assert.Equal(HttpStatusCode.OK, (await Post("/v1/heartbeat", Hello)).StatusCode);
        Assert.Equal(PremiereConnectionState.Connected, _bridge.Connection.State);
        _now = _now.AddSeconds(11);
        Assert.Equal(PremiereConnectionState.ConnectionProblem, _bridge.Connection.State);
        Assert.Equal(HttpStatusCode.Conflict, (await Post("/v1/heartbeat", Hello with { Protocol = 2 })).StatusCode);
        Assert.Equal(PremiereConnectionState.UpdateRequired, _bridge.Connection.State);
    }
    [Theory]
    [InlineData("localhost:47858", false, "127.0.0.1")]
    [InlineData("127.0.0.1:47857", false, "127.0.0.1")]
    [InlineData("localhost:47857", true, "127.0.0.1")]
    [InlineData("localhost:47857", false, "192.168.1.20")]
    public void AuthenticationRejectsWrongEndpointBrowserAndRemote(string host, bool browser, string address)
        => Assert.False(_bridge.Authenticate(host, _authorization, browser, IPAddress.Parse(address)));
    [Fact]
    public async Task RotationAndExpiryInvalidateOldTokens()
    {
        Assert.True(_bridge.Authenticate("localhost:47857", _authorization, false, IPAddress.Loopback));
        await _bridge.RotatePairingAsync();
        Assert.False(_bridge.Authenticate("localhost:47857", _authorization, false, IPAddress.Loopback));
        _now = _now.AddHours(9);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post("/v1/heartbeat", Hello)).StatusCode);
    }
    [Fact]
    public async Task EnvironmentCannotAddAnExtraListener()
    {
        await _bridge.DisposeAsync();
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var extraPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        const string key = "Kestrel__Endpoints__Unexpected__Url";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, $"http://0.0.0.0:{extraPort}");
            _bridge = new(_journal, Path.Combine(_temp, "pairing"), () => _now);
            await _bridge.StartAsync();
            using var client = new System.Net.Sockets.TcpClient();
            await Assert.ThrowsAsync<System.Net.Sockets.SocketException>(() => client.ConnectAsync(IPAddress.Loopback, extraPort));
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }
    [Fact]
    public async Task HttpBoundaryRejectsBrowserRequestsUnauthenticatedAndArbitraryCommands()
    {
        _client.DefaultRequestHeaders.Add("Origin", "https://example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post("/v1/heartbeat", Hello)).StatusCode);
        _client.DefaultRequestHeaders.Remove("Origin");
        _client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post("/v1/heartbeat", Hello)).StatusCode);
        _client.DefaultRequestHeaders.Remove("Sec-Fetch-Site");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/v1/execute", new { command = "shell" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/v1/poll")).StatusCode);
        _client.DefaultRequestHeaders.Remove("Authorization");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post("/v1/heartbeat", Hello)).StatusCode);
    }
    [Fact]
    public async Task DurableIntentDispatchAndReceiptSurviveNewRepositoryInstance()
    {
        var first = await _journal.PrepareAsync(_project, "root", null, _source);
        Assert.Equal(_session.Identity.CatalogId, first.Intent.CatalogId);
        Assert.False(first.PreviouslyDispatched);
        await _journal.MarkDispatchedAsync(first.Intent);
        var reopened = new CatalogPremiereHandoffs(() => _session);
        var ranged = _source with { Range = new PremiereRangeProjection("10", "90", "100") };
        var unknown = await reopened.PrepareAsync(_project, "editor-moved-bin", null, ranged);
        Assert.Equal(first.Intent.OperationId, unknown.Intent.OperationId);
        Assert.Equal("editor-moved-bin", unknown.Intent.BinId);
        Assert.Equal(ranged.Range, unknown.Intent.Source.Range);
        Assert.True(unknown.PreviouslyDispatched);
        var receipt = new PremiereReceipt(first.Intent.OperationId, PremiereOutcome.Verified, "item-1", "verified");
        await reopened.SaveReceiptAsync(first.Intent, receipt);
        Assert.Equal(receipt, (await reopened.PrepareAsync(_project, "root", null, _source)).PreviousReceipt);
    }
    [Fact]
    public async Task ChangedSourceAndWrongCatalogCannotOverwriteOperation()
    {
        var first = await _journal.PrepareAsync(_project, "root", null, _source);
        await File.AppendAllTextAsync(_source.Path, "changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.PrepareAsync(_project, "root", null, _source));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _journal.MarkDispatchedAsync(first.Intent with { CatalogId = Guid.NewGuid() }));
    }
    [Fact]
    public async Task FailedReceiptCannotEraseEstablishedItemIdentity()
    {
        var command = await _journal.PrepareAsync(_project, "root", null, _source);
        await _journal.SaveReceiptAsync(command.Intent, new(command.Intent.OperationId, PremiereOutcome.Verified, "stable-item", "verified"));
        await _journal.SaveReceiptAsync(command.Intent, new(command.Intent.OperationId, PremiereOutcome.Failed, null, "connection changed"));
        Assert.Equal("stable-item", (await _journal.PrepareAsync(_project, "root", null, _source)).PreviousReceipt!.ItemId);
    }
    [Fact]
    public async Task NoProjectAndStaleSessionsNeverAuthorizeHandoff()
    {
        Assert.Equal(HttpStatusCode.OK, (await Post("/v1/heartbeat", Hello with { Project = null, Bins = [] })).StatusCode);
        Assert.Equal(PremiereConnectionState.Connected, _bridge.Connection.State);
        var command = await _journal.PrepareAsync(_project, "root", null, _source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _bridge.SendAsync(command, CancellationToken.None));
        await Post("/v1/heartbeat", Hello);
        Assert.Equal(HttpStatusCode.Conflict, (await Post("/v1/heartbeat", Hello with { InstanceId = "second" })).StatusCode);
        _now = _now.AddSeconds(11);
        Assert.Equal(HttpStatusCode.OK, (await Post("/v1/heartbeat", Hello with { InstanceId = "second" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post("/v1/poll", new { })).StatusCode);
    }
    [Fact]
    public async Task CancellationBeforeDispatchLeavesIntentSafeToSend()
    {
        await Post("/v1/heartbeat", Hello);
        var command = await _journal.PrepareAsync(_project, "root", null, _source);
        using var cancel = new CancellationTokenSource();
        var pending = _bridge.SendAsync(command, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/v1/poll", new { })).StatusCode);
        Assert.False((await _journal.PrepareAsync(_project, "root", null, _source)).PreviouslyDispatched);
    }
    [Fact]
    public async Task JobsSerializeHandoffsCancelQueuedWorkAndExposeDurableReceipts()
    {
        await Post("/v1/heartbeat", Hello);
        var jobs = new PremiereJobs(_journal, _bridge);
        jobs.Enqueue(_project, "root", null, [_source]);
        jobs.Enqueue(_project, "root", null, [_source]);
        var queued = jobs.Jobs[1];
        Assert.False(queued.WorkspaceItem().CanPause);
        Assert.False(queued.WorkspaceItem().CanReorder);
        jobs.Cancel(queued.JobId);
        PremiereCommand? dispatched = null;
        for (var attempt = 0; attempt < 30 && dispatched is null; attempt++)
        {
            var response = await Post("/v1/poll", new { });
            if (response.StatusCode == HttpStatusCode.OK)
                dispatched = await response.Content.ReadFromJsonAsync<PremiereCommand>(PremiereProtocol.Json);
        }
        Assert.NotNull(dispatched);
        _client.DefaultRequestHeaders.Add("X-Lightflow-Dispatch", dispatched.DispatchId.ToString());
        var receipt = new PremiereReceipt(dispatched.Intent.OperationId, PremiereOutcome.Verified, "native-item", "verified source");
        Assert.Equal(HttpStatusCode.OK, (await Post("/v1/receipt", receipt)).StatusCode);
        for (var attempt = 0; attempt < 100 && jobs.Jobs.Any(job => job.State is JobState.Queued or JobState.Running); attempt++)
            await Task.Delay(20);
        Assert.Equal(JobState.Completed, jobs.Jobs[0].State);
        Assert.Equal(receipt, Assert.Single(jobs.Jobs[0].Receipts));
        Assert.Equal(JobState.Cancelled, jobs.Jobs[1].State);
        await jobs.RefreshHistoryAsync();
        Assert.Equal(JobState.Completed, Assert.Single(jobs.History).State);
        Assert.Single(await _journal.ListAsync());
    }
    [Fact]
    public async Task SubclipJobReconcilesSourceBeforeDispatchingNativeCreateAndRequiresProjectionProof()
    {
        await Post("/v1/heartbeat", Hello);
        var projection = new PremiereSubclipProjection(Guid.NewGuid(), "Native", 1,
            new("10", "90", "100"), "");
        var jobs = new PremiereJobs(_journal, _bridge);
        jobs.EnqueueSubclips(_project, "root", null, [new(_source, projection)]);

        PremiereCommand? source = null;
        for (var attempt = 0; attempt < 30 && source is null; attempt++)
        {
            var response = await Post("/v1/poll", new { });
            if (response.StatusCode == HttpStatusCode.OK)
                source = await response.Content.ReadFromJsonAsync<PremiereCommand>(PremiereProtocol.Json);
        }
        Assert.NotNull(source);
        Assert.Null(source.Intent.Subclip);
        Assert.True(source.Intent.Source.IsSubclipPrerequisite);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(source,
            new(source.Intent.OperationId, PremiereOutcome.Verified, "source-item", "source imported",
                Verification: PremiereProtocol.TemporarySubclipSourceVerification))).StatusCode);

        PremiereCommand? native = null;
        for (var attempt = 0; attempt < 30 && native is null; attempt++)
        {
            var response = await Post("/v1/poll", new { });
            if (response.StatusCode == HttpStatusCode.OK)
                native = await response.Content.ReadFromJsonAsync<PremiereCommand>(PremiereProtocol.Json);
        }
        Assert.NotNull(native);
        Assert.Equal("source-item", native.Intent.Subclip!.SourceItemId);
        Assert.True(native.Intent.Subclip.RemoveSourceAfter);
        var sourceShaped = new PremiereReceipt(native.Intent.OperationId, PremiereOutcome.Verified,
            "native-item", "accepted without proof");
        Assert.Equal(HttpStatusCode.BadRequest, (await PostReceipt(native, sourceShaped)).StatusCode);
        for (var attempt = 0; attempt < 100 && jobs.Jobs[0].State is JobState.Queued or JobState.Running; attempt++)
            await Task.Delay(20);
        Assert.Equal(JobState.CompletedWithWarnings, jobs.Jobs[0].State);
    }

    [Fact]
    public async Task ExistingOrdinarySourceNeverAuthorizesSubclipPrerequisiteCleanup()
    {
        await Post("/v1/heartbeat", Hello);
        var jobs = new PremiereJobs(_journal, _bridge);
        jobs.EnqueueSubclips(_project, "root", null,
        [
            new(_source, new(Guid.NewGuid(), "Existing source moment", 1, new("10", "90", "100"), ""))
        ]);

        var source = await PollCommandAsync();
        Assert.True(source.Intent.Source.IsSubclipPrerequisite);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(source,
            new(source.Intent.OperationId, PremiereOutcome.Verified, "editor-source", "existing source verified"))).StatusCode);
        var native = await PollCommandAsync();
        Assert.False(native.Intent.Subclip!.RemoveSourceAfter);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(native,
            new(native.Intent.OperationId, PremiereOutcome.Verified, "native-item", "created",
                PremiereProtocol.SubclipProjectionKey(native.Intent.Subclip), "native-subclip-v3"))).StatusCode);
        for (var attempt = 0; attempt < 100 && jobs.Jobs[0].State is JobState.Queued or JobState.Running; attempt++)
            await Task.Delay(20);

        Assert.Equal(JobState.Completed, jobs.Jobs[0].State);
    }

    [Fact]
    public async Task SameSourceSubclipBatchDispatchesAndCompletesEverySubclipIndependently()
    {
        await Post("/v1/heartbeat", Hello);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var jobs = new PremiereJobs(_journal, _bridge);
        jobs.EnqueueSubclips(_project, "root", null,
        [
            new(_source, new(firstId, "First moment", 1, new("10", "40", "100"), "")),
            new(_source, new(secondId, "Second moment", 1, new("50", "90", "100"), ""))
        ]);

        var source = await PollCommandAsync();
        Assert.Null(source.Intent.Subclip);
        Assert.True(source.Intent.Source.IsSubclipPrerequisite);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(source,
            new(source.Intent.OperationId, PremiereOutcome.Verified, "source-item", "source verified",
                Verification: PremiereProtocol.TemporarySubclipSourceVerification))).StatusCode);
        var first = await PollCommandAsync();
        Assert.Equal(firstId, first.Intent.Subclip!.SubclipId);
        Assert.False(first.Intent.Subclip.RemoveSourceAfter);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(first,
            new(first.Intent.OperationId, PremiereOutcome.Verified, "native-first", "created",
                PremiereProtocol.SubclipProjectionKey(first.Intent.Subclip), "native-subclip-v3"))).StatusCode);
        var second = await PollCommandAsync();
        Assert.Equal(secondId, second.Intent.Subclip!.SubclipId);
        Assert.True(second.Intent.Subclip.RemoveSourceAfter);
        Assert.NotEqual(first.Intent.OperationId, second.Intent.OperationId);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(second,
            new(second.Intent.OperationId, PremiereOutcome.Verified, "native-second", "created",
                PremiereProtocol.SubclipProjectionKey(second.Intent.Subclip), "native-subclip-v3"))).StatusCode);
        for (var attempt = 0; attempt < 100 && jobs.Jobs[0].State is JobState.Queued or JobState.Running; attempt++)
            await Task.Delay(20);

        var job = jobs.Jobs[0];
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(2, job.Completed);
        Assert.Equal(2, job.Receipts.Select(receipt => receipt.OperationId).Distinct().Count());
        Assert.Collection(job.Items!,
            item => { Assert.Equal("First moment", item.Name); Assert.Equal(PremiereJobItemState.Sent, item.State); },
            item => { Assert.Equal("Second moment", item.Name); Assert.Equal(PremiereJobItemState.Sent, item.State); });
        Assert.Contains("First moment — Sent", job.Details);
        Assert.Contains("Second moment — Sent", job.Details);
    }

    [Fact]
    public async Task FailedDependentSubclipDoesNotAuthorizeTemporarySourceCleanup()
    {
        await Post("/v1/heartbeat", Hello);
        var jobs = new PremiereJobs(_journal, _bridge);
        jobs.EnqueueSubclips(_project, "root", null,
        [
            new(_source, new(Guid.NewGuid(), "Blocked moment", 1, new("10", "40", "100"), "")),
            new(_source, new(Guid.NewGuid(), "Later moment", 1, new("50", "90", "100"), ""))
        ]);

        var source = await PollCommandAsync();
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(source,
            new(source.Intent.OperationId, PremiereOutcome.Verified, "temporary-source", "source imported",
                Verification: PremiereProtocol.TemporarySubclipSourceVerification))).StatusCode);
        var first = await PollCommandAsync();
        Assert.False(first.Intent.Subclip!.RemoveSourceAfter);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(first,
            new(first.Intent.OperationId, PremiereOutcome.Conflict, null, "projection conflict"))).StatusCode);
        var second = await PollCommandAsync();
        Assert.False(second.Intent.Subclip!.RemoveSourceAfter);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(second,
            new(second.Intent.OperationId, PremiereOutcome.Verified, "native-second", "created",
                PremiereProtocol.SubclipProjectionKey(second.Intent.Subclip), "native-subclip-v3"))).StatusCode);
        for (var attempt = 0; attempt < 100 && jobs.Jobs[0].State is JobState.Queued or JobState.Running; attempt++)
            await Task.Delay(20);

        Assert.Equal(JobState.CompletedWithWarnings, jobs.Jobs[0].State);
    }

    [Fact]
    public async Task MixedSourceBatchMapsSharedPreparationFailureToItsFallbackItemAndWarns()
    {
        await Post("/v1/heartbeat", Hello);
        var fallbackSource = await CreateSourceAsync("fallback.mov");
        var nativeId = Guid.NewGuid();
        var jobs = new PremiereJobs(_journal, _bridge);
        jobs.EnqueueSubclips(_project, "root", null,
        [
            new(_source, new(nativeId, "Native moment", 1, new("10", "90", "100"), "")),
            new(fallbackSource, new(null, "fallback.mov", 1, null, "", IsSourceFallback: true))
        ]);

        var firstSource = await PollCommandAsync();
        Assert.Equal(_source.AssetId, firstSource.Intent.Source.AssetId);
        Assert.True(firstSource.Intent.Source.IsSubclipPrerequisite);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(firstSource,
            new(firstSource.Intent.OperationId, PremiereOutcome.Verified, "source-one", "source verified",
                Verification: PremiereProtocol.TemporarySubclipSourceVerification))).StatusCode);
        var native = await PollCommandAsync();
        Assert.True(native.Intent.Subclip!.RemoveSourceAfter);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(native,
            new(native.Intent.OperationId, PremiereOutcome.Verified, "native-one", "created",
                PremiereProtocol.SubclipProjectionKey(native.Intent.Subclip!), "native-subclip-v3"))).StatusCode);
        var secondSource = await PollCommandAsync();
        Assert.Equal(fallbackSource.AssetId, secondSource.Intent.Source.AssetId);
        Assert.False(secondSource.Intent.Source.IsSubclipPrerequisite);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(secondSource,
            new(secondSource.Intent.OperationId, PremiereOutcome.Failed, null, "source mutation missing"))).StatusCode);
        for (var attempt = 0; attempt < 100 && jobs.Jobs[0].State is JobState.Queued or JobState.Running; attempt++)
            await Task.Delay(20);

        var job = jobs.Jobs[0];
        Assert.Equal(JobState.CompletedWithWarnings, job.State);
        Assert.Equal(2, job.Completed);
        Assert.Collection(job.Items!,
            item => { Assert.Equal("Native moment", item.Name); Assert.Equal(PremiereJobItemState.Sent, item.State); },
            item => { Assert.Equal("fallback.mov", item.Name); Assert.Equal(PremiereJobItemState.Failed, item.State); });
        Assert.Contains("Native moment — Sent", job.Details);
        Assert.Contains("fallback.mov — Failed", job.Details);
        Assert.DoesNotContain("Existing video verified", job.Details);
        await jobs.RefreshHistoryAsync();
        Assert.Empty(jobs.History);
    }

    [Fact]
    public async Task WholeSourceFallbackCompletesAfterSourceReconciliationWithoutNativeCommand()
    {
        await Post("/v1/heartbeat", Hello);
        var fallback = new PremiereSubclipProjection(null, "source", 1, null, "", IsSourceFallback: true);
        var jobs = new PremiereJobs(_journal, _bridge);
        jobs.EnqueueSubclips(_project, "root", null, [new(_source, fallback)]);
        PremiereCommand? source = null;
        for (var attempt = 0; attempt < 30 && source is null; attempt++)
        {
            var response = await Post("/v1/poll", new { });
            if (response.StatusCode == HttpStatusCode.OK)
                source = await response.Content.ReadFromJsonAsync<PremiereCommand>(PremiereProtocol.Json);
        }
        Assert.NotNull(source);
        Assert.Null(source.Intent.Subclip);
        Assert.Null(source.Intent.Source.Range);
        Assert.False(source.Intent.Source.IsSubclipPrerequisite);
        Assert.Equal(HttpStatusCode.OK, (await PostReceipt(source,
            new(source.Intent.OperationId, PremiereOutcome.Verified, "source-item", "Source imported and verified."))).StatusCode);
        for (var attempt = 0; attempt < 100 && jobs.Jobs[0].State is JobState.Queued or JobState.Running; attempt++)
            await Task.Delay(20);
        Assert.Equal(JobState.Completed, jobs.Jobs[0].State);
        Assert.Equal("Send 1 video to Premiere", jobs.Jobs[0].Name);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/v1/poll", new { })).StatusCode);
    }
    [Fact]
    public async Task CommandIsDispatchedOnceAndReceiptIsBoundToOperationAndSession()
    {
        await Post("/v1/heartbeat", Hello);
        var command = await _journal.PrepareAsync(_project, "root", null, _source);
        var completion = _bridge.SendAsync(command, CancellationToken.None);
        Assert.False(_bridge.HasUnresolvedDispatchedHandoff);
        var response = await Post("/v1/poll", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_bridge.HasUnresolvedDispatchedHandoff);
        Assert.True((await _journal.PrepareAsync(_project, "root", null, _source)).PreviouslyDispatched);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/v1/poll", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post("/v1/receipt", new PremiereReceipt(Guid.NewGuid(), PremiereOutcome.Verified, "item", "wrong"))).StatusCode);
        var receipt = new PremiereReceipt(command.Intent.OperationId, PremiereOutcome.Verified, "item", "verified");
        _client.DefaultRequestHeaders.Add("X-Lightflow-Dispatch", command.DispatchId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await Post("/v1/receipt", receipt)).StatusCode);
        Assert.Equal(receipt, await completion);
        Assert.False(_bridge.HasUnresolvedDispatchedHandoff);
    }

    [Fact]
    public void SourceRangePlanningUsesTheNearestPremiereTickOrLetsUserSendTheFullSource()
    {
        var range = new MediaRange(TimeSpan.FromTicks(100), TimeSpan.FromTicks(10), TimeSpan.FromTicks(90));
        Assert.True(PremiereRangeProjection.TryCreate(range, out var projection));
        Assert.NotNull(projection);
        Assert.True(projection!.IsValid());
        var withRange = _source with { Range = projection };
        Assert.Same(withRange, Assert.Single(PremiereSendPlanning.Sources([withRange], true)));
        Assert.Null(Assert.Single(PremiereSendPlanning.Sources([withRange], false)).Range);
        Assert.True(PremiereRangeProjection.TryCreate(new MediaRange(TimeSpan.FromTicks(101), TimeSpan.FromTicks(10), TimeSpan.FromTicks(91)), out var adjusted));
        Assert.NotNull(adjusted);
        Assert.True(adjusted!.IsValid());
    }
    [Fact]
    public async Task ProjectSwitchBeforeDispatchStopsWithoutImportPermission()
    {
        await Post("/v1/heartbeat", Hello);
        var command = await _journal.PrepareAsync(_project, "root", null, _source);
        var completion = _bridge.SendAsync(command, CancellationToken.None);
        await Post("/v1/heartbeat", Hello with { Project = _project with { Path = @"C:\copy.prproj" } });
        Assert.Equal(HttpStatusCode.Conflict, (await Post("/v1/poll", new { })).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => completion);
        Assert.False((await _journal.PrepareAsync(_project, "root", null, _source)).PreviouslyDispatched);
    }
    [Theory]
    [InlineData("0 extensions installed for Others", (int)PremiereConnectionState.PremiereNotInstalled)]
    [InlineData("0 extensions installed for Premiere Pro (ver 26.5.0)\n Status Extension Name Version\n", (int)PremiereConnectionState.CompanionNotInstalled)]
    [InlineData("1 extension installed for Premiere Pro (ver 26.5.0)\n Enabled com.lightflowstudio.premiere 1.1.5\n", (int)PremiereConnectionState.Ready)]
    [InlineData("1 extension installed for Premiere Pro (ver 26.5.0)\n Enabled com.lightflowstudio.premiere 1.1.2\n", (int)PremiereConnectionState.UpdateRequired)]
    [InlineData("1 extension installed for Premiere Pro (ver 26.5.0)\n Enabled com.lightflowstudio.premiere 1.1.1\n", (int)PremiereConnectionState.UpdateRequired)]
    [InlineData("1 extension installed for Premiere Pro (ver 26.5.0)\n Enabled com.lightflowstudio.premiere 1.1.0\n", (int)PremiereConnectionState.UpdateRequired)]
    [InlineData("1 extension installed for Premiere Pro (ver 26.5.0)\n Enabled com.lightflowstudio.premiere 1.0.6\n", (int)PremiereConnectionState.UpdateRequired)]
    [InlineData("1 extension installed for Premiere Pro (ver 26.5.0)\n Enabled com.lightflowstudio.premiere 2.0.0\n", (int)PremiereConnectionState.UpdateRequired)]
    [InlineData("System exception", (int)PremiereConnectionState.ConnectionProblem)]
    public void InstallationInventoryDoesNotInventLiveness(string inventory, int expected)
        => Assert.Equal((PremiereConnectionState)expected, PremiereInstallation.ParseInventory(inventory).State);
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_bridge is not null) await _bridge.DisposeAsync();
        if (_session is not null) await _session.DisposeAsync();
        try { Directory.Delete(_temp, true); } catch (IOException) { }
    }
}
