using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using Lightflow.Application;
using Lightflow.Domain;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

/// <summary>
/// Read-only native filesystem facts. Protected roots are supplied by composition, never inferred
/// from the role or a user's profile. Missing configuration cannot establish containment.
/// </summary>
public sealed class MacStorageLocationAssessor : IStorageLocationAssessor, IDisposable
{
    private readonly object _gate = new();
    private StorageContextHandle? _nativeContext;
    private bool _disposed;
    private readonly string[] _protectedRoots;
    private readonly TimeProvider _time;
    private readonly bool _allowMediaSourceAncestor;
    private readonly TimeSpan _lifetime;

    public MacStorageLocationAssessor(IEnumerable<string> protectedRoots, TimeProvider? timeProvider = null,
        TimeSpan? lifetime = null, bool allowMediaSourceAncestor = false)
    {
        ArgumentNullException.ThrowIfNull(protectedRoots);
        _protectedRoots = protectedRoots.ToArray();
        _allowMediaSourceAncestor = allowMediaSourceAncestor;
        if (_protectedRoots.Any(p => string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p) || p.Contains('\0')))
            throw new ArgumentException("Protected roots must be explicit absolute filesystem paths.", nameof(protectedRoots));
        _time = timeProvider ?? TimeProvider.System;
        _lifetime = lifetime ?? TimeSpan.FromSeconds(5);
        if (_lifetime <= TimeSpan.Zero || _lifetime > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
    }

    public Task<StorageLocationAssessment> AssessAsync(StorageAssessmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Native metadata calls cannot be interrupted safely. Run off the calling thread and check
        // cancellation on both sides. The consumer must also check it before actual admission.
        return Task.Run(() => { lock (_gate) { return Assess(request, cancellationToken); } }, CancellationToken.None);
    }

    private StorageLocationAssessment Assess(StorageAssessmentRequest request, CancellationToken token)
    {
        var started = _time.GetUtcNow();
        StorageLocationAssessment Failure(string diagnostic, bool cancelled = false) => new(request,
            Guid.NewGuid(), started, started + _lifetime, StorageLocationPolicy.Version,
            cancelled ? StorageAssessmentStatus.Cancelled : StorageAssessmentStatus.Failed, null,
            StorageLocality.Unknown, StorageAvailability.Unknown, StorageAvailability.Unknown,
            StorageResolutionConfidence.Unknown, StorageResolutionConfidence.Unknown, null, diagnostic);
        if (token.IsCancellationRequested) return Failure("Cancelled before native assessment.", true);
        if (_disposed) return Failure("The assessor was disposed; obtain facts from the active composition owner.");
        if (!OperatingSystem.IsMacOS()) return Failure("This assessor requires macOS and the native storage library.");
        if (string.IsNullOrWhiteSpace(request.RequestedLocation) || request.RequestedLocation.Contains('\0')
            || !Path.IsPathFullyQualified(request.RequestedLocation))
            return Failure("Supply an absolute NUL-free path; caller spelling is retained unchanged.");
        try
        {
            _ = new UTF8Encoding(false, true).GetByteCount(request.RequestedLocation);
            _nativeContext ??= NativeCreate();
            if (_nativeContext.IsInvalid) return Failure("Native mount context unavailable.");
            var create = request.Operation is StorageOperation.Create or StorageOperation.Relocate
                or StorageOperation.RestoreActivation or StorageOperation.Write;
            using var probe = Probe(request.RequestedLocation, create, _allowMediaSourceAncestor && request.Role == StorageRole.MediaSource && request.Operation == StorageOperation.Read);
            var facts = probe.RootElement;
            if (facts.TryGetProperty("error", out var error))
            {
                var action = facts.TryGetProperty("errno", out var nativeError) ? nativeError.GetInt32() switch
                {
                    13 or 1 => " Check filesystem permissions; do not recreate the Catalog.",
                    2 => " Reconnect storage or confirm the explicit path; absence does not authorize creation.",
                    30 => " The mount is read-only; choose a qualified writable location explicitly.",
                    _ => " Retry native assessment without changing existing storage."
                } : " Preserve existing storage and restart preflight.";
                return Failure(error.GetString() + action);
            }
            var locality = Enum.Parse<StorageLocality>(facts.GetProperty("locality").GetString()!);
            var canonical = facts.GetProperty("canonical").GetString()!;
            var ambiguous = facts.GetProperty("ambiguous").GetBoolean();
            var containment = _protectedRoots.Length > 0 && !ambiguous;
            foreach (var root in _protectedRoots)
            {
                using var boundary = Probe(root, true);
                var b = boundary.RootElement;
                if (b.TryGetProperty("error", out _) || b.GetProperty("ambiguous").GetBoolean()
                    || b.GetProperty("missingLeaf").GetString() != "")
                { containment = false; continue; }
                var protectedPath = b.GetProperty("canonical").GetString()!;
                // Default storage assessment refuses both nesting directions. Browser-only
                // MediaSource anchors may contain owned storage; actual scans exclude it.
                // Equal/inside protected objects and ambiguous identities remain refused.
                var mediaAncestor = _allowMediaSourceAncestor && request.Role == StorageRole.MediaSource;
                if ((mediaAncestor ? canonical == protectedPath || canonical.StartsWith(protectedPath.TrimEnd('/') + "/", StringComparison.Ordinal) : Overlaps(canonical, protectedPath))
                    || facts.GetProperty("ancestors").EnumerateArray().Any(v => v.GetString() == b.GetProperty("target").GetString())
                    || (!mediaAncestor && facts.GetProperty("missingLeaf").GetString() == "" && b.GetProperty("ancestors").EnumerateArray().Any(v => v.GetString() == facts.GetProperty("target").GetString()))
                    || (facts.GetProperty("missingLeaf").GetString() == ""
                        && b.GetProperty("missingLeaf").GetString() == ""
                        && facts.GetProperty("target").GetString() == b.GetProperty("target").GetString()))
                    containment = false;
            }
            if (token.IsCancellationRequested) return Failure("Cancelled after native observation; no admission.", true);
            var read = Capability(facts.GetProperty("read").GetBoolean());
            var write = Capability(facts.GetProperty("write").GetBoolean());
            var filesystem = facts.GetProperty("filesystem").GetString()!;
            var locking = facts.GetProperty("lockingKnown").GetBoolean()
                ? Capability(facts.GetProperty("locking").GetBoolean()) : StorageCapability.Unknown;
            // G2 qualifies the bounded native APFS case, not all local writable filesystems.
            // Case-sensitive APFS and external durability remain outside the accepted evidence.
            var qualified = locality == StorageLocality.Local && filesystem == "apfs"
                && facts.GetProperty("internalKnown").GetBoolean() && facts.GetProperty("internal").GetBoolean()
                && facts.GetProperty("caseKnown").GetBoolean() && !facts.GetProperty("caseSensitive").GetBoolean();
            var qualification = qualified ? StorageCapability.Supported : StorageCapability.Unknown;
            var fsid = facts.GetProperty("fsid").GetString()!;
            var mount = facts.GetProperty("mount").GetString()!;
            var source = facts.GetProperty("source").GetString()!;
            var epoch = facts.GetProperty("mountEpoch").GetString()!;
            var identity = new ResolvedStorageIdentity(canonical,
                JsonSerializer.Serialize(new { target = facts.GetProperty("target").GetString(),
                    parent = facts.GetProperty("parent").GetString(), leaf = facts.GetProperty("missingLeaf").GetString() }),
                JsonSerializer.Serialize(new { fsid, mount, source, epoch }), $"{filesystem}:{fsid}");
            return new(request, Guid.NewGuid(), started, started + _lifetime, StorageLocationPolicy.Version,
                StorageAssessmentStatus.Complete, identity, locality, StorageAvailability.Available,
                StorageAvailability.Available, ambiguous ? StorageResolutionConfidence.Ambiguous : StorageResolutionConfidence.Resolved,
                containment ? StorageResolutionConfidence.Resolved : StorageResolutionConfidence.Ambiguous,
                new(read, write, qualification, qualification, locking, qualification),
                "Native metadata snapshot; linked/cloud/unknown containment fails closed. APFS qualification is bounded by G2, not a power-loss guarantee. Fresh actual-use guards remain required.");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or JsonException or IOException or InvalidOperationException or ArgumentException)
        {
            return Failure($"Native provider failure ({e.GetType().Name}); preserve storage and retry assessment.");
        }
    }

    private static bool Overlaps(string a, string b) => a == b || a.StartsWith(b.TrimEnd('/') + "/", StringComparison.Ordinal)
        || b.StartsWith(a.TrimEnd('/') + "/", StringComparison.Ordinal);
    private static StorageCapability Capability(bool supported) => supported ? StorageCapability.Supported : StorageCapability.Unsupported;

    private JsonDocument Probe(string path, bool permitMissingLeaf, bool mediaRead = false)
    {
        var pointer = mediaRead ? NativeMediaProbe(_nativeContext!, path) : NativeProbe(_nativeContext!, path, permitMissingLeaf ? 1 : 0);
        if (pointer == IntPtr.Zero) throw new IOException("Native probe returned no facts.");
        try { return JsonDocument.Parse(Marshal.PtrToStringUTF8(pointer)!); }
        finally { NativeFree(pointer); }
    }

    internal bool IsUnrelatedOfflineRoot(string candidate, string existing, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _nativeContext ??= NativeCreate();
            using var facts = MediaResult(NativeUnrelatedOffline(_nativeContext, candidate, existing));
            token.ThrowIfCancellationRequested();
            return facts.RootElement.TryGetProperty("unrelated", out var unrelated) && unrelated.GetBoolean();
        }
    }
    internal bool IsResidentMedia(string path, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
            _nativeContext ??= NativeCreate();
            var pointer=NativeMediaProbe(_nativeContext,path);
            if(pointer==IntPtr.Zero)throw new IOException("Native media residency unavailable; retry without downloading sources.");
            try
            {
                using var facts=JsonDocument.Parse(Marshal.PtrToStringUTF8(pointer)!);
                token.ThrowIfCancellationRequested();
                if(facts.RootElement.TryGetProperty("cloudUnavailable",out var cloud) && cloud.GetBoolean())return false;
                if(facts.RootElement.TryGetProperty("error",out var error))
                {
                    if(facts.RootElement.TryGetProperty("errno",out var code))
                    {
                        if(code.GetInt32()==2)throw new DirectoryNotFoundException(error.GetString());
                        if(code.GetInt32() is 1 or 13)throw new UnauthorizedAccessException(error.GetString());
                    }
                    throw new IOException(error.GetString());
                }
                if(facts.RootElement.GetProperty("ambiguous").GetBoolean())throw new IOException("Unsafe media identity (alias, link or unresolved spelling). Review the source path, then retry.");
                if(!facts.RootElement.GetProperty("read").GetBoolean())throw new UnauthorizedAccessException("Media is not readable. Check source permissions outside Lightflow, then retry.");
                return true;
            }
            finally {NativeFree(pointer);}
        }
    }
    internal SourceFingerprint ResidentFingerprint(string path, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _nativeContext ??= NativeCreate();
            using var facts = MediaResult(NativeFingerprint(_nativeContext, path));
            token.ThrowIfCancellationRequested();
            return new(SampledSourceFingerprintService.CurrentVersion, facts.RootElement.GetProperty("fingerprint").GetString()!);
        }
    }
    private static JsonDocument MediaResult(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) throw new IOException("Native media evidence unavailable; retry without changing Catalog records.");
        try
        {
            var result = JsonDocument.Parse(Marshal.PtrToStringUTF8(pointer)!);
            if (result.RootElement.TryGetProperty("error", out var error))
            { var message = error.GetString(); result.Dispose(); throw new IOException(message); }
            return result;
        }
        finally { NativeFree(pointer); }
    }
    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_media_probe")]
    private static extern IntPtr NativeMediaProbe(StorageContextHandle context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_media_unrelated_offline")]
    private static extern IntPtr NativeUnrelatedOffline(StorageContextHandle context, [MarshalAs(UnmanagedType.LPUTF8Str)] string candidate, [MarshalAs(UnmanagedType.LPUTF8Str)] string existing);
    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_media_fingerprint")]
    private static extern IntPtr NativeFingerprint(StorageContextHandle context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_storage_probe")]
    private static extern IntPtr NativeProbe(StorageContextHandle context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int permitMissingLeaf);
    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_storage_free")]
    private static extern void NativeFree(IntPtr pointer);

    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_storage_create")]
    private static extern StorageContextHandle NativeCreate();
    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_storage_destroy")]
    private static extern void NativeDestroy(IntPtr context);

    public void Dispose()
    {
        lock (_gate)
        {
            _nativeContext?.Dispose();
            _nativeContext = null;
            _disposed = true;
        }
    }

    private sealed class StorageContextHandle : SafeHandle
    {
        public StorageContextHandle() : base(IntPtr.Zero, ownsHandle: true) { }
        public override bool IsInvalid => handle == IntPtr.Zero;
        protected override bool ReleaseHandle() { NativeDestroy(handle); return true; }
    }
}
