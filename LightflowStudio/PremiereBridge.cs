using System.IO;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace LightflowStudio;

internal enum PremiereConnectionState
{
    PremiereNotInstalled, CompanionNotInstalled, Ready, Connected, UpdateRequired, ConnectionProblem
}

internal sealed record PremiereConnection(PremiereConnectionState State, string Message, PremiereHello? Companion = null);

/// <summary>Lightflow hosts only a typed command/receipt protocol; no filesystem or shell RPC is exposed.</summary>
internal sealed class PremiereBridge : IAsyncDisposable
{
    private readonly CatalogPremiereHandoffs _journal;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly string _pairingDirectory;
    private WebApplication? _server;
    private string _token = "";
    private DateTimeOffset _expires;
    private DateTimeOffset _heartbeat;
    private PremiereHello? _hello;
    private bool _incompatible;
    private string? _problem;
    private PremiereCommand? _pending;
    private TaskCompletionSource<PremiereReceipt>? _completion;
    private string? _dispatchSession;
    private Guid _dispatchId;
    private long _lastRequest;
    private bool _disposed;
    private readonly CancellationTokenSource _maintenanceStop = new();
    private Task? _maintenance;
    public const int HeartbeatSeconds = 10;
    public event Action? Changed;

    public PremiereBridge(CatalogPremiereHandoffs journal, string pairingDirectory, Func<DateTimeOffset>? now = null)
    { _journal = journal; _pairingDirectory = pairingDirectory; _now = now ?? (() => DateTimeOffset.UtcNow); }

    public string PairingDirectory => _pairingDirectory;
    public PremiereConnection Connection
    {
        get
        {
            if (_problem is not null) return new(PremiereConnectionState.ConnectionProblem, _problem);
            if (_incompatible) return new(PremiereConnectionState.UpdateRequired, "Install companion 1.x and Premiere Pro 26.5 or later.");
            var hello = _hello;
            if (hello is not null && _now() < _expires && _now() - _heartbeat < TimeSpan.FromSeconds(HeartbeatSeconds))
                return new(PremiereConnectionState.Connected, $"Connected — Premiere Pro {hello.HostVersion}", hello);
            if (hello is not null) return new(PremiereConnectionState.ConnectionProblem,
                "The companion is not responding. Open Window > UXP Plugins > Lightflow Studio Companion in Premiere. It reconnects automatically after setup.");
            return new(PremiereConnectionState.Ready, "In Premiere, choose Window > UXP Plugins > Lightflow Studio Companion. After first-time setup, it connects automatically.");
        }
    }

    public async Task StartAsync()
    {
        if (_server is not null) return;
        // No environment configuration, proxy middleware, logging providers, CORS, redirects or URL overrides.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        // Discard appsettings/environment endpoint configuration before Kestrel reads it.
        // A desktop companion bridge must never acquire an additional non-loopback listener.
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, PremiereProtocol.Port, listen => listen.Protocols = HttpProtocols.Http1);
            options.Limits.MaxRequestBodySize = 64 * 1024;
            options.Limits.MaxRequestHeadersTotalSize = 8192;
            options.Limits.MaxConcurrentConnections = 4;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(5);
        });
        var server = builder.Build();
        server.Run(HandleAsync);
        try
        {
            await server.StartAsync().ConfigureAwait(false);
            _server = server;
            await RotatePairingAsync().ConfigureAwait(false);
            _maintenance = MaintainPairingAsync();
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            _server = null;
            _problem = "Could not start the Premiere bridge. Check whether another Lightflow instance is using port 47857.";
            Changed?.Invoke();
            throw;
        }
    }

    private async Task MaintainPairingAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(_maintenanceStop.Token).ConfigureAwait(false))
            {
                try { await RenewExpiredPairingAsync().ConfigureAwait(false); }
                catch (Exception) when (!_disposed) { _problem = "The companion connection could not be renewed. Open Integration Settings and reset the connection."; Changed?.Invoke(); }
            }
        }
        catch (OperationCanceledException) when (_maintenanceStop.IsCancellationRequested) { }
    }
    public Task RotatePairingAsync() => RotatePairingAsync(false);
    internal Task RenewExpiredPairingAsync() => RotatePairingAsync(true);
    private async Task RotatePairingAsync(bool onlyIfExpired)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || (onlyIfExpired && _now() < _expires)) return;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expires = _now().AddHours(8);
            var directory = Directory.CreateDirectory(_pairingDirectory);
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Pairing folder must not be a link.");
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.SetOwner(WindowsIdentity.GetCurrent().User!);
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
            var path = Path.Combine(_pairingDirectory, "lightflow-pairing.json");
            var temp = Path.Combine(_pairingDirectory, Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new { endpoint = PremiereProtocol.Endpoint,
                protocol = PremiereProtocol.Version, token, expiresUtc = expires }, PremiereProtocol.Json)).ConfigureAwait(false);
            File.Move(temp, path, true);
            _token = token;
            _expires = expires;
            _hello = null;
            _incompatible = false;
            _problem = null;
            _completion?.TrySetException(new InvalidOperationException("Pairing rotated. Reconcile the interrupted handoff."));
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    internal bool Authenticate(string host, string? authorization, bool browserRequest, IPAddress? remote)
    {
        if (host != $"localhost:{PremiereProtocol.Port}" || browserRequest || remote is null
            || !IPAddress.IsLoopback(remote) || _now() >= _expires || _token.Length != 64) return false;
        var expected = Encoding.ASCII.GetBytes("Bearer " + _token);
        var actual = Encoding.ASCII.GetBytes(authorization ?? "");
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        context.Response.Headers.CacheControl = "no-store";
        if (!Authenticate(request.Host.Value, request.Headers.Authorization.ToString(),
            request.Headers.ContainsKey("Origin") || request.Headers.Keys.Any(key => key.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase)),
            context.Connection.RemoteIpAddress)) { context.Response.StatusCode = 403; return; }
        if (request.Method != "POST" || request.QueryString.HasValue || request.ContentType != "application/json"
            || request.Path.Value is not ("/v1/heartbeat" or "/v1/poll" or "/v1/receipt"))
        { context.Response.StatusCode = 400; return; }
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) { context.Response.StatusCode = 429; return; }
        try
        {
            var current = Environment.TickCount64;
            if (current - _lastRequest < 50) { context.Response.StatusCode = 429; return; }
            _lastRequest = current;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            if (request.Path == "/v1/heartbeat")
            {
                var hello = await JsonSerializer.DeserializeAsync<PremiereHello>(request.Body, PremiereProtocol.Json, timeout.Token);
                if (hello is null || string.IsNullOrWhiteSpace(hello.InstanceId) || hello.InstanceId.Length > 100
                    || hello.Bins is null || hello.Bins.Count > 1000 || string.IsNullOrWhiteSpace(hello.UxpVersion)
                    || string.IsNullOrWhiteSpace(hello.CompanionVersion) || string.IsNullOrWhiteSpace(hello.HostVersion)
                    || hello.Project is { } project && (string.IsNullOrWhiteSpace(project.Guid)
                        || string.IsNullOrWhiteSpace(project.Path) || !Path.IsPathFullyQualified(project.Path)))
                { context.Response.StatusCode = 400; return; }
                _incompatible = hello.Protocol != PremiereProtocol.Version || !Version.TryParse(hello.CompanionVersion, out var companionVersion)
                    || companionVersion.Major != 1
                    || !PremiereProtocol.SupportedHost(hello.HostVersion);
                if (_incompatible) { context.Response.StatusCode = 409; Changed?.Invoke(); return; }
                if (_hello is not null && _hello.InstanceId != hello.InstanceId && Connection.State == PremiereConnectionState.Connected)
                { context.Response.StatusCode = 409; return; }
                _hello = hello;
                _heartbeat = _now();
                Changed?.Invoke();
                await context.Response.WriteAsJsonAsync(new { protocol = 1 }, PremiereProtocol.Json, timeout.Token);
                return;
            }
            if (Connection.State != PremiereConnectionState.Connected)
            { context.Response.StatusCode = 409; return; }
            if (request.Headers["X-Lightflow-Session"].ToString() != _hello!.InstanceId)
            { context.Response.StatusCode = 403; return; }
            if (request.Path == "/v1/poll")
            {
                if (_pending is null) { context.Response.StatusCode = 204; return; }
                var pending = _pending;
                if (!SameProject(_hello.Project, pending.Intent.Project))
                { _completion?.TrySetException(new InvalidOperationException("Active Premiere project changed. Retry only in the original destination.")); context.Response.StatusCode = 409; return; }
                CatalogPremiereHandoffs.ValidateSource(pending.Intent.Source);
                // Persist intent as dispatched before giving Premiere permission to mutate.
                await _journal.MarkDispatchedAsync(pending.Intent).ConfigureAwait(false);
                _dispatchSession = _hello.InstanceId;
                _pending = null;
                await context.Response.WriteAsJsonAsync(pending, PremiereProtocol.Json, timeout.Token);
            }
            else
            {
                var receipt = await JsonSerializer.DeserializeAsync<PremiereReceipt>(request.Body, PremiereProtocol.Json, timeout.Token);
                if (receipt is null || _activeIntent is null || _dispatchSession != _hello.InstanceId
                    || request.Headers["X-Lightflow-Dispatch"].ToString() != _dispatchId.ToString()
                    || receipt.OperationId != _activeIntent.OperationId)
                { context.Response.StatusCode = 409; return; }
                await _journal.SaveReceiptAsync(_activeIntent, receipt).ConfigureAwait(false);
                _completion?.TrySetResult(receipt);
                await context.Response.WriteAsJsonAsync(new { accepted = true }, PremiereProtocol.Json, timeout.Token);
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or IOException or OperationCanceledException)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 400;
            _completion?.TrySetException(new InvalidOperationException("Bridge request failed. Reconnect and reconcile before retrying."));
        }
        finally { _gate.Release(); }
    }

    private PremiereIntent? _activeIntent;
    public async Task<PremiereReceipt> SendAsync(PremiereCommand command, CancellationToken cancellationToken)
    {
        await _send.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (Connection.State != PremiereConnectionState.Connected || !SameProject(_hello?.Project, command.Intent.Project))
                    throw new InvalidOperationException("Reconnect to the original Premiere project before sending.");
                _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _activeIntent = command.Intent;
                _pending = command;
                _dispatchId = command.DispatchId;
                _dispatchSession = null;
            }
            finally { _gate.Release(); }
            // Cancellation ends waiting only; an in-flight Premiere import cannot be atomically undone.
            return await _completion.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { _pending = null; _completion = null; _activeIntent = null; }
            finally { _gate.Release(); _send.Release(); }
        }
    }

    internal static bool SameProject(PremiereProject? left, PremiereProject right) => left is not null
        && left.Guid == right.Guid && PremiereProtocol.PathKey(left.Path) == PremiereProtocol.PathKey(right.Path);

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _maintenanceStop.Cancel();
        if (_maintenance is not null) await _maintenance.ConfigureAwait(false);
        _token = "";
        _completion?.TrySetException(new InvalidOperationException("Lightflow is closing. Reconcile any interrupted handoff."));
        if (_server is not null) await _server.DisposeAsync().ConfigureAwait(false);
    }
}
