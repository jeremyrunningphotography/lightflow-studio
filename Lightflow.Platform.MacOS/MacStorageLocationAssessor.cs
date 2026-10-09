using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using Lightflow.Application;
using Lightflow.Domain;

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
    private readonly TimeSpan _lifetime;

    public MacStorageLocationAssessor(IEnumerable<string> protectedRoots, TimeProvider? timeProvider = null,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(protectedRoots);
        _protectedRoots = protectedRoots.ToArray();
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
            using var probe = Probe(request.RequestedLocation, create);
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
                // Roots represent storage of other ownership classes. Refuse either nesting
                // direction; identical underlying objects also fail despite different spelling.
                if (Overlaps(canonical, protectedPath)
                    || facts.GetProperty("ancestors").EnumerateArray().Any(v => v.GetString() == b.GetProperty("target").GetString())
                    || (facts.GetProperty("missingLeaf").GetString() == "" && b.GetProperty("ancestors").EnumerateArray().Any(v => v.GetString() == facts.GetProperty("target").GetString()))
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

    private JsonDocument Probe(string path, bool permitMissingLeaf)
    {
        var pointer = NativeProbe(_nativeContext!, path, permitMissingLeaf ? 1 : 0);
        if (pointer == IntPtr.Zero) throw new IOException("Native probe returned no facts.");
        try { return JsonDocument.Parse(Marshal.PtrToStringUTF8(pointer)!); }
        finally { NativeFree(pointer); }
    }

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
