using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using Lightflow.Application;
using Lightflow.Domain;
using Microsoft.Win32.SafeHandles;

namespace LightflowStudio;

/// <summary>
/// Operation-owned Windows observations. Retained directory handles exclude delete/rename during
/// admission, and each boundary probes both the retained binding and a fresh requested-path handle.
/// Dispose after the operation; the operation epoch is deliberately not a persistent volume ID.
/// </summary>
internal sealed class WindowsStorageLocationAssessor(IEnumerable<string>? protectedDirectories = null,
    IReadOnlyDictionary<string, StorageRole>? protectedRoles = null)
    : IStorageLocationAssessor, IDisposable
{
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SafeFileHandle> _createdTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SafeFileHandle> _pins = [];
    private readonly Guid _epoch = Guid.NewGuid();
    private readonly string[] _protectedDirectories = protectedDirectories?.ToArray() ?? [];
    private bool _disposed;
    private SafeFileHandle? _mainGuard;
    private FileInformation? _qualifiedMain;
    private string? _guardIdentity;

    public Task<StorageLocationAssessment> AssessAsync(StorageAssessmentRequest request,
        CancellationToken cancellationToken = default) => Task.Run(() => Assess(request, cancellationToken), cancellationToken);

    private StorageLocationAssessment Assess(StorageAssessmentRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var phase = "ValidateRequestedPath";
        string? resolved = null;
        var accessDiagnostics = new List<string>();
        var containmentDiagnostics = new List<string>();
        var assessmentId = Guid.NewGuid();
        var order = 0;
        var nativeProbe = "not-started";
        StorageLocationAssessment Snapshot(StorageAssessmentStatus status, ResolvedStorageIdentity? identity,
            StorageLocality locality, StorageAvailability available, StorageResolutionConfidence aliases,
            StorageResolutionConfidence containment, StorageCapabilityFacts? capabilities, string? diagnostic = null) =>
            new(request, assessmentId, now, now.AddSeconds(30), StorageLocationPolicy.Version, status,
                identity, locality, available, available, aliases, containment, capabilities, diagnostic);
        void Predicate(string kind, string target, string facts) => containmentDiagnostics.Add(
            $"predicate={kind}; phase={phase}; order={order}; observedAt={DateTimeOffset.UtcNow:O}; probe=GetFileInformationByHandle/File.GetAttributes; target=[{TargetContext(target)}]; {facts}");
        try
        {
            var path = ValidatePath(request.RequestedLocation);
            phase = "ResolveAndPinRequestedTarget";
            var binding = Resolve(path, cancellationToken);
            phase = "ObserveResolvedTarget";
            var observed = Observe(binding.Handle);
            var canonical = Join(observed.Canonical, binding.Suffix);
            resolved = canonical;
            var locality = observed.Locality;
            var identity = new ResolvedStorageIdentity(canonical,
                $"{observed.FileId}|{binding.Suffix}", $"{_epoch:N}|{observed.Volume}", observed.FileSystem);
            var accessPath = Directory.Exists(path) ? path : binding.Parent;
            phase = "ProbeTargetAccess";
            var read = Access(accessPath, 1, accessDiagnostics); // FILE_LIST_DIRECTORY; does not create files.
            var write = (observed.Flags & 0x80000) != 0 ? StorageCapability.Unsupported
                : Access(accessPath, 2 | 4, accessDiagnostics); // FILE_ADD_FILE / FILE_ADD_SUBDIRECTORY.
            var containment = StorageResolutionConfidence.Resolved;
            foreach (var boundary in _protectedDirectories)
            {
                order++;
                phase = "ResolveProtectedBoundary";
                cancellationToken.ThrowIfCancellationRequested();
                var other = Resolve(ValidatePath(boundary), cancellationToken);
                var otherObserved = Observe(other.Handle);
                if (LightflowStorageLocations.PathsOverlap(canonical, Join(otherObserved.Canonical, other.Suffix)))
                {
                    containment = StorageResolutionConfidence.Ambiguous;
                    Predicate("ProtectedRootOverlap", canonical, $"probe=ResolvedPathsOverlap; protectedRole={(protectedRoles is not null && protectedRoles.TryGetValue(boundary, out var role) ? role.ToString() : "unspecified")}; protected=[{TargetContext(boundary)}]; protectedResolved=[{TargetContext(Join(otherObserved.Canonical, other.Suffix))}]; protectedIdentity=[{TargetContext(otherObserved.FileId)}]");
                }
            }
            // Catalog leaves/sidecars must not independently escape the directory through links.
            if (request.Role == StorageRole.ActiveCatalog && Directory.Exists(path))
                foreach (var leaf in new[] { "LightflowCatalog.db", "LightflowCatalog.db-wal", "LightflowCatalog.db-shm", "LightflowCatalog.db-journal" })
                {
                    order++;
                    phase = "InspectCatalogLeaf";
                    var file = Path.Combine(path, leaf);
                    try
                    {
                        nativeProbe = "File.GetAttributes";
                        var attributes = File.GetAttributes(file);
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                            containment = StorageResolutionConfidence.Ambiguous;
                        var attributeFacts = $"leaf={leaf}; attributes=0x{(uint)attributes:X}; probe=File.GetAttributes";
                        if ((attributes & FileAttributes.Directory) != 0) Predicate("LeafDirectory", file, attributeFacts);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) Predicate("LeafReparse", file, attributeFacts);
                        nativeProbe = "CreateFileW/MetadataNoFollow";
                        using var handle = Open(file, 0, reparse: true);
                        nativeProbe = "GetFileInformationByHandle";
                        var information = Information(handle);
                        var facts = $"leaf={leaf}; attributes=0x{(uint)attributes:X}; handleAttributes=0x{information.Attributes:X}; links={information.Links}; identity=[{TargetContext(FileIdentity(information))}]";
                        if ((attributes & FileAttributes.Directory) != 0) Predicate("LeafDirectory", file, facts);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) Predicate("LeafReparse", file, facts);
                        if (information.Links != 1)
                        {
                            containment = StorageResolutionConfidence.Ambiguous;
                            Predicate("LeafLinkCount", file, facts);
                        }
                        if (leaf == "LightflowCatalog.db")
                        {
                            if (_mainGuard is not null && (FileIdentity(information) != _guardIdentity ||
                                FileIdentity(Information(_mainGuard)) != _guardIdentity))
                                throw new IOException("The guarded main Catalog object changed. Refuse activation.");
                            if (_mainGuard is not null)
                                StartupDiagnostics.Note($"Catalog main guard: freshMatch=verified; identity=[{TargetContext(_guardIdentity)}]; operationId={request.OperationId:N}; generation={request.Generation}; assessmentId={assessmentId:N}; order={order}");
                            _qualifiedMain = information;
                            read = Access(file, 0x80000000, accessDiagnostics);
                            if (write == StorageCapability.Supported) write = Access(file, 0x40000000, accessDiagnostics);
                        }
                    }
                    catch (Win32Exception error) when (error.NativeErrorCode is 2 or 3)
                    { if (leaf == "LightflowCatalog.db" && _mainGuard is not null) throw new IOException("The guarded main Catalog became unavailable.", error); }
                    catch (FileNotFoundException)
                    { if (leaf == "LightflowCatalog.db" && _mainGuard is not null) throw new IOException("The guarded main Catalog became unavailable."); }
                    catch (DirectoryNotFoundException)
                    { if (leaf == "LightflowCatalog.db" && _mainGuard is not null) throw new IOException("The guarded main Catalog became unavailable."); }
                }
            // Accepted G2 Windows evidence is local NTFS. Transport (Fixed/Removable) is not
            // qualification. These are OS filesystem semantics, not hardware power-loss claims.
            // Require the native disk/file identity and the NTFS capability profile observed
            // by G2, including persistent ACLs and Unicode names. A name alone is insufficient.
            phase = "ObserveFileSystemQualification";
            var qualified = locality != StorageLocality.Local || observed.Name != "NTFS"
                ? StorageCapability.Unsupported
                : GetFileType(binding.Handle) == 1 && (observed.Flags & 0xC) == 0xC
                    ? StorageCapability.Supported : StorageCapability.Unknown;
            var semantics = qualified == StorageCapability.Supported ? StorageCapability.Supported : StorageCapability.Unknown;
            cancellationToken.ThrowIfCancellationRequested();
            return Snapshot(StorageAssessmentStatus.Complete, identity, locality, StorageAvailability.Available,
                StorageResolutionConfidence.Resolved, containment,
                new(read, write, qualified, semantics, semantics, semantics),
                containmentDiagnostics.Count > 0 ? DescribeFailure(request, phase, resolved, $"assessmentId={assessmentId:N}; " + string.Join("; ", containmentDiagnostics.Concat(accessDiagnostics)))
                    : accessDiagnostics.Count > 0 ? DescribeFailure(request, "CapabilityAccessProbe", resolved, string.Join("; ", accessDiagnostics))
                    : qualified == StorageCapability.Supported || locality == StorageLocality.Network || request.Role != StorageRole.ActiveCatalog ? null
                    : qualified == StorageCapability.Unknown
                        ? $"Required filesystem capabilities could not be verified for '{observed.Name}'. Check access or choose a supported local NTFS location."
                        : $"Active Catalogs require a supported local NTFS filesystem. The resolved filesystem is '{observed.Name}'.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var nativeError = error as Win32Exception ?? error.InnerException as Win32Exception;
            // Unreachable paths are never evidence of an absent/first-run Catalog.
            var unavailable = error is Win32Exception native && native.NativeErrorCode is 2 or 3 or 15 or 21 or 53 or 67 or 1117 or 1167;
            return Snapshot(StorageAssessmentStatus.Complete, null,
                unavailable ? StorageLocality.Unavailable : StorageLocality.Unknown,
                unavailable ? StorageAvailability.Unavailable : StorageAvailability.Available,
                StorageResolutionConfidence.Unknown, StorageResolutionConfidence.Unknown, null,
                DescribeFailure(request, phase, resolved,
                    $"assessmentId={assessmentId:N}; order={order}; lastProbe={nativeProbe}; {string.Join("; ", containmentDiagnostics)}; {error.Message}; exception={error.GetType().Name}; win32={(nativeError?.NativeErrorCode.ToString() ?? (error is UnauthorizedAccessException ? "5" : "not-native"))}" +
                    (nativeError is not null && nativeError != error ? $"; causedBy={nativeError.Message}" : "")));
        }
    }

    internal void GuardMainDatabase(string databasePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mainGuard is not null) return;
        if (_qualifiedMain is not { } qualified) throw new IOException("Fully qualify the main Catalog before acquiring its identity guard.");
        var guard = Open(databasePath, 0x80000000, reparse: true); // share READ|WRITE, excludes DELETE; never a sidecar pin.
        try
        {
            var current = Information(guard);
            if ((current.Attributes & (0x400 | 0x10)) != 0 || current.Links != 1 || FileIdentity(current) != FileIdentity(qualified))
                throw new IOException("The main Catalog changed while acquiring its identity guard.");
            _guardIdentity = FileIdentity(current);
            _mainGuard = guard;
            StartupDiagnostics.Note($"Catalog main guard: acquired; identity=[{TargetContext(_guardIdentity)}]; target=[{TargetContext(databasePath)}]; share=ReadWrite; deleteRename=excluded");
        }
        catch { guard.Dispose(); throw; }
    }

    internal void ReleaseMainDatabaseGuard()
    {
        _mainGuard?.Dispose();
        _mainGuard = null; _guardIdentity = null; _qualifiedMain = null;
    }

    private static string FileIdentity(FileInformation information) =>
        $"{information.Volume:X8}:{information.IndexHigh:X8}{information.IndexLow:X8}";

    // Correlation tokens retain requested/resolved distinction without publishing user paths.
    internal static string TargetContext(string? path) => path is null ? "not-observed"
        : $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16]};length={path.Length}";
    private static string DescribeFailure(StorageAssessmentRequest request, string phase, string? resolved, string detail) =>
        $"Windows assessment: phase={phase}; operation={request.Operation}; operationId={request.OperationId:N}; generation={request.Generation}; " +
        $"requested=[{TargetContext(request.RequestedLocation)}]; resolved=[{TargetContext(resolved)}]; {detail}";
    private static Win32Exception NativeFailure(string api, int code, string? target = null) =>
        new(code, $"probe={api}; win32={code}; target=[{TargetContext(target)}]; {new Win32Exception(code).Message}");

    private Binding Resolve(string path, CancellationToken cancellationToken)
    {
        if (_bindings.TryGetValue(path, out var prior))
        {
            // The old handle must still be live (a forced dismount invalidates it); then freshly
            // reopen its original spelling so a drive-letter/alias substitution is detectable.
            var old = Observe(prior.Handle);
            using var fresh = Open(prior.Parent, 0);
            var current = Observe(fresh);
            if (old != current) throw new IOException("The resolved volume, mount or directory changed. Restart the storage operation.");
            using var target = TryOpen(path);
            if (target is not null)
            {
                var targetObserved = Observe(target);
                if (targetObserved.Canonical != Join(current.Canonical, prior.Suffix))
                    throw new IOException("The intended Catalog target changed. Restart the storage operation.");
                if (_createdTargets.TryGetValue(path, out var retainedTarget))
                {
                    if (Observe(retainedTarget) != targetObserved)
                        throw new IOException("The created directory was replaced. Restart the storage operation.");
                }
                else if (prior.Suffix.Length != 0)
                {
                    // Once the operation creates its intended directory, bind that new object
                    // before creating/opening SQLite there. Parent/suffix alone cannot protect
                    // a replacement directory at the same spelling.
                    var created = Open(path, 0x80000000);
                    _pins.Add(created);
                    if (Observe(created) != targetObserved)
                        throw new IOException("The created directory changed while binding it.");
                    PinAncestors(path, cancellationToken);
                    PinAncestors(targetObserved.Canonical, cancellationToken);
                    _createdTargets.Add(path, created);
                }
            }
            else if (_createdTargets.ContainsKey(path))
                throw new IOException("The created directory became unavailable. Restart the storage operation.");
            return prior;
        }
        var parent = path;
        var suffix = new Stack<string>();
        SafeFileHandle handle;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { handle = Open(parent, 0); break; }
            catch (Win32Exception error) when (error.NativeErrorCode is 2 or 3)
            {
                suffix.Push(Path.GetFileName(parent));
                parent = Path.GetDirectoryName(parent) ?? throw new IOException("No accessible creation parent exists.", error);
            }
        }
        _pins.Add(handle);
        if ((Information(handle).Attributes & 0x10) == 0) throw new IOException("The selected storage path is a file, not a folder.");
        var observed = Observe(handle);
        // A resolved remote target is already disqualified for active use. Its provider need
        // not expose traversal rights on every share ancestor merely to explain that refusal.
        if (observed.Locality != StorageLocality.Network)
        {
            PinAncestors(parent, cancellationToken);
            if (observed.Locality == StorageLocality.Local) PinAncestors(observed.Canonical, cancellationToken);
        }
        var binding = new Binding(parent, string.Join('\\', suffix), handle);
        _bindings.Add(path, binding);
        return binding;
    }

    private void PinAncestors(string path, CancellationToken cancellationToken)
    {
        for (string? ancestor = path; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Metadata-only (access=0) handles do not participate in normal share denial.
            // Directory read handles do, so rename/delete exclusion is effective.
            var pin = Open(ancestor, 0x80000000, reparse: true);
            _pins.Add(pin);
            if ((Information(pin).Attributes & 0x400) != 0)
            {
                if (!GetFileInformationByHandleEx(pin, 9, out var tag, 8))
                    throw NativeFailure("GetFileInformationByHandleEx(FileAttributeTagInfo)", Marshal.GetLastWin32Error(), ancestor);
                if (tag.Tag is not (0xA0000003 or 0xA000000C))
                    throw new NotSupportedException($"Reparse tag {tag.Tag:X8} cannot be resolved with qualified Catalog semantics.");
                // Pin the link object against write-based reparse retargeting, separately from
                // its resolved target. Ordinary child writes through the target remain allowed.
                _pins.Add(Open(ancestor, 0x80000000, reparse: true, shareWrite: false));
            }
            if (ancestor == Path.GetPathRoot(ancestor)) break;
        }
    }

    private static string ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            path.Split('\\', '/').Any(part => part.EndsWith(' ') || part.EndsWith('.')))
            throw new ArgumentException("Choose an absolute, unambiguous filesystem folder.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static string Join(string parent, string suffix) => Path.TrimEndingDirectorySeparator(
        suffix.Length == 0 ? parent : Path.Combine(parent, suffix));
    private static SafeFileHandle? TryOpen(string path)
    {
        try { return Open(path, 0); }
        catch (Win32Exception error) when (error.NativeErrorCode is 2 or 3) { return null; }
    }
    private static SafeFileHandle Open(string path, uint access, bool reparse = false, bool shareWrite = true)
    {
        var handle = CreateFileW(path, access, shareWrite ? 3u : 1u, IntPtr.Zero, 3, 0x02000000u | (reparse ? 0x00200000u : 0), IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error(); handle.Dispose();
        throw NativeFailure($"CreateFileW(access=0x{access:X},reparse={reparse},shareWrite={shareWrite})", error, path);
    }
    private static StorageCapability Access(string path, uint access, List<string> diagnostics)
    {
        try { using var handle = Open(path, access); return StorageCapability.Supported; }
        catch (Win32Exception error) when (error.NativeErrorCode is 5 or 19) { diagnostics.Add(error.Message); return StorageCapability.Unsupported; }
        catch (Win32Exception error) { diagnostics.Add(error.Message); return StorageCapability.Unknown; }
    }
    private static FileInformation Information(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            var error = Marshal.GetLastWin32Error();
            throw NativeFailure("GetFileInformationByHandle", error);
        }
        return information;
    }
    private static Observation Observe(SafeFileHandle handle)
    {
        var path = new StringBuilder(32768);
        // VOLUME_NAME_GUID provides a volume-qualified path unaffected by drive-letter spelling.
        var local = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 1);
        StorageLocality locality;
        string volume;
        if (local > 0 && local < path.Capacity && path.ToString().StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
        {
            volume = path.ToString()[..(path.ToString().IndexOf('}') + 2)];
            var type = GetDriveTypeW(volume);
            locality = type is 2 or 3 ? StorageLocality.Local : type == 4 ? StorageLocality.Network : StorageLocality.Unknown;
        }
        else
        {
            path.Clear();
            var length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
            if (length == 0 || length >= path.Capacity) throw NativeFailure("GetFinalPathNameByHandleW(DOS)", Marshal.GetLastWin32Error());
            var resolved = path.ToString();
            var resolvedRoot = Path.GetPathRoot(resolved);
            locality = resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ||
                resolvedRoot is not null && GetDriveTypeW(resolvedRoot) == 4 ? StorageLocality.Network : StorageLocality.Unknown;
            volume = locality == StorageLocality.Network ? string.Join('\\', resolved.Split('\\').Take(6)) : "unresolved";
        }
        var filesystem = new StringBuilder(256);
        var filesystemKnown = GetVolumeInformationByHandleW(handle, null, 0, out var serial, out _, out var flags, filesystem, (uint)filesystem.Capacity);
        if (!filesystemKnown && locality != StorageLocality.Network)
        {
            var error = Marshal.GetLastWin32Error();
            throw NativeFailure("GetVolumeInformationByHandleW", error, path.ToString());
        }
        var information = Information(handle);
        var name = filesystem.ToString();
        return new(Path.TrimEndingDirectorySeparator(path.ToString()), $"{serial:X8}:{information.IndexHigh:X8}{information.IndexLow:X8}",
            $"{volume}|{serial:X8}", filesystemKnown ? $"{volume}|{serial:X8}|{name}" : "", name, flags, locality);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseMainDatabaseGuard();
        foreach (var pin in _pins) pin.Dispose();
        _pins.Clear(); _bindings.Clear(); _createdTargets.Clear();
    }
    private sealed record Binding(string Parent, string Suffix, SafeFileHandle Handle);
    private sealed record Observation(string Canonical, string FileId, string Volume, string FileSystem,
        string Name, uint Flags, StorageLocality Locality);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag { public uint Attributes, Tag; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTag tag, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle handle, StringBuilder? label,
        uint labelSize, out uint serial, out uint maximumComponentLength, out uint flags, StringBuilder filesystem, uint filesystemSize);
}
