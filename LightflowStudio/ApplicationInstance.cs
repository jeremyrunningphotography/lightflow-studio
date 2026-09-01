using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace LightflowStudio;

internal sealed record ApplicationLaunchRequest(int Version, string[] Arguments)
{
    public const int CurrentVersion = 1;

    public static ApplicationLaunchRequest Current(IEnumerable<string> arguments) =>
        new(CurrentVersion, arguments.ToArray());
}

internal enum ApplicationInstanceStatus
{
    Primary,
    ExistingInstanceActivated,
    ExistingInstanceActivationFailed
}

internal sealed record ApplicationInstanceResult(ApplicationInstanceStatus Status, string? Diagnostic = null);

internal interface IApplicationInstanceCoordinator : IDisposable
{
    event Action<ApplicationLaunchRequest>? LaunchRequested;
    ApplicationInstanceResult StartOrSignal(ApplicationLaunchRequest request);
}

/// <summary>
/// Windows application-instance boundary. The stable identity is deliberately independent of the executable path;
/// the Local namespace and current-user-only pipe scope ownership to the interactive user/session.
/// </summary>
internal sealed class WindowsApplicationInstanceCoordinator : IApplicationInstanceCoordinator
{
    internal const string ApplicationIdentity = "JeremyRunningPhotography.LightflowStudio.Application";
    private const int MaximumPayloadBytes = 64 * 1024;
    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly TimeSpan _connectionTimeout;
    private readonly CancellationTokenSource _listenerCancellation = new();
    private Mutex? _mutex;
    private Task? _listenerTask;
    private bool _ownsMutex;
    private bool _started;

    public WindowsApplicationInstanceCoordinator(string identity = ApplicationIdentity, TimeSpan? connectionTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        _mutexName = $"Local\\{identity}";
        _pipeName = identity;
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(5);
        if (_connectionTimeout <= TimeSpan.Zero || _connectionTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout));
    }

    public event Action<ApplicationLaunchRequest>? LaunchRequested;

    public ApplicationInstanceResult StartOrSignal(ApplicationLaunchRequest request)
    {
        ObjectDisposedException.ThrowIf(_listenerCancellation.IsCancellationRequested, this);
        if (_started) throw new InvalidOperationException("Application instance coordination has already started.");
        _started = true;

        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew) _ownsMutex = TryAcquireOwnership();

        if (_ownsMutex)
        {
            _listenerTask = ListenAsync(_listenerCancellation.Token);
            return new(ApplicationInstanceStatus.Primary);
        }

        try
        {
            SignalExistingInstance(request);
            _mutex!.Dispose();
            _mutex = null;
            return new(ApplicationInstanceStatus.ExistingInstanceActivated);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException
            or InvalidDataException or JsonException)
        {
            // The prior owner may have crashed between the initial mutex check and IPC connection attempt.
            // Acquiring the mutex here proves there is no competing owner, so normal startup is safe.
            if ((_ownsMutex = TryAcquireOwnership()))
            {
                _listenerTask = ListenAsync(_listenerCancellation.Token);
                return new(ApplicationInstanceStatus.Primary);
            }
            var diagnostic = $"Another Lightflow Studio instance owns application startup, but activation signaling failed: {exception.Message}";
            Trace.WriteLine(diagnostic);
            _mutex!.Dispose();
            _mutex = null;
            return new(ApplicationInstanceStatus.ExistingInstanceActivationFailed, diagnostic);
        }
    }

    private bool TryAcquireOwnership()
    {
        try { return _mutex!.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException) { return true; }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var request = await ReadRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
                LaunchRequested?.Invoke(request);
                pipe.WriteByte(1);
                await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or JsonException)
            {
                Trace.WriteLine($"Lightflow Studio application activation listener failed: {exception}");
            }
        }
    }

    private void SignalExistingInstance(ApplicationLaunchRequest request)
    {
        using var cancellation = new CancellationTokenSource();
        var signaling = SignalExistingInstanceAsync(request, cancellation.Token);
        var completed = Task.WhenAny(signaling, Task.Delay(_connectionTimeout)).GetAwaiter().GetResult();
        if (completed == signaling)
        {
            signaling.GetAwaiter().GetResult();
            return;
        }

        // The asynchronous operation owns its pipe until the OS eventually completes or tears it down. Observing
        // it in the background avoids both an unobserved fault and a synchronous Dispose that can itself hang on
        // Windows while an acknowledgement read is pending. A losing launcher can now always exit on deadline.
        cancellation.Cancel();
        _ = signaling.ContinueWith(task => _ = task.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        throw new TimeoutException("The active Lightflow Studio instance did not acknowledge activation in time.");
    }

    private async Task SignalExistingInstanceAsync(ApplicationLaunchRequest request, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(request);
        if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("The launch request is too large.");
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await pipe.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        var acknowledgement = new byte[1];
        var acknowledgementLength = await pipe.ReadAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
        if (acknowledgementLength != 1 || acknowledgement[0] != 1)
            throw new IOException("The active Lightflow Studio instance did not acknowledge activation.");
    }

    private static async Task<ApplicationLaunchRequest> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is <= 0 or > MaximumPayloadBytes) throw new InvalidDataException("The launch request length is invalid.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<ApplicationLaunchRequest>(payload)
            ?? throw new InvalidDataException("The launch request is empty.");
        if (request.Version != ApplicationLaunchRequest.CurrentVersion)
            throw new InvalidDataException($"Launch request version {request.Version} is unsupported.");
        return request;
    }

    public void Dispose()
    {
        if (!_listenerCancellation.IsCancellationRequested) _listenerCancellation.Cancel();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _ownsMutex = false;
        _mutex?.Dispose();
        _mutex = null;
        _listenerCancellation.Dispose();
    }
}
