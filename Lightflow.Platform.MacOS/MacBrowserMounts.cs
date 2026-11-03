using System.Runtime.InteropServices;
using System.Text.Json;
using Lightflow.Application;

namespace Lightflow.Platform.MacOS;

/// <summary>Native mount facts; display policy never grants Catalog storage eligibility.</summary>
public sealed record MacBrowserMount(string Path, string Name, string VolumeId, string Source,
    string FileSystem, bool Local, bool? Internal);

public static class MacBrowserMounts
{
    public static string Anchor(string path, IEnumerable<string> mounts) => mounts
        .Select(Path.TrimEndingDirectorySeparator)
        .Where(mount => (mount == "/" || mount.StartsWith("/Volumes/", StringComparison.Ordinal)) &&
            (path == mount || path.StartsWith(mount == "/" ? "/" : mount + "/", StringComparison.Ordinal)))
        .OrderByDescending(mount => mount.Length).FirstOrDefault()
        ?? throw new IOException("This folder has no available volume boundary.");

    public static IReadOnlyList<MacBrowserMount> Read()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var pointer = NativeRead();
        if (pointer == IntPtr.Zero) throw new IOException("Native mount table unavailable.");
        try { return JsonSerializer.Deserialize<MacBrowserMount[]>(Marshal.PtrToStringUTF8(pointer)!)
            ?? throw new IOException("Native mount table unavailable."); }
        finally { NativeFree(pointer); }
    }
    public static IReadOnlyList<BrowserFileLocation> Locations(string home, IReadOnlyList<MacBrowserMount> mounts)
    {
        var result = new List<BrowserFileLocation>
        {
            new(home, Path.GetFileName(home.TrimEnd('/')), false, BrowserLocationSection.Favorites, BrowserLocationKind.Home)
        };
        foreach (var (name, kind) in new[] { ("Desktop", BrowserLocationKind.Desktop),
            ("Documents", BrowserLocationKind.Documents), ("Downloads", BrowserLocationKind.Downloads) })
            result.Add(new(Path.Combine(home,name), name, false, BrowserLocationSection.Favorites, kind));
        var system = mounts.FirstOrDefault(mount => mount.Path == "/");
        foreach (var mount in mounts.DistinctBy(mount => mount.Path, StringComparer.Ordinal))
        {
            // Suppress only native-established duplicates of the actual startup volume.
            // No suffix/name-based APFS deduplication. Hidden Data mounts are excluded by
            // native browsability; distinct user volumes with equal names remain separate.
            if (mount.Path != "/" && system is not null &&
                ((mount.VolumeId.Length > 0 && mount.VolumeId == system.VolumeId) ||
                 (mount.Source.Length > 0 && mount.Source == system.Source))) continue;
            var kind = !mount.Local ? BrowserLocationKind.NetworkShare : mount.Path == "/"
                ? BrowserLocationKind.SystemVolume : mount.Internal == false
                ? BrowserLocationKind.ExternalVolume : BrowserLocationKind.LocalVolume;
            var name = string.IsNullOrWhiteSpace(mount.Name)
                ? mount.Path == "/" ? "System Volume" : Path.GetFileName(mount.Path) : mount.Name;
            result.Add(new(mount.Path, name, false,
                mount.Local ? BrowserLocationSection.Devices : BrowserLocationSection.Network,
                kind, mount.Local ? null : VerifiedServer(mount)));
        }
        return result;
    }
    public static string? VerifiedServer(MacBrowserMount mount)
    {
        // Only the kernel SMB mount source establishes this relationship. Do not use
        // display names, DNS guesses or network scans; other formats get a flat share.
        if (mount.Local || mount.FileSystem != "smbfs" || !mount.Source.StartsWith("//", StringComparison.Ordinal)) return null;
        var source = mount.Source[2..];
        var slash = source.IndexOf('/');
        if (slash <= 0 || slash == source.Length - 1) return null;
        var authority = source[..slash];
        var host = authority[(authority.LastIndexOf('@') + 1)..];
        // Uri validates host/port/IPv6 syntax without performing network I/O.
        if (!Uri.TryCreate("smb://" + host + "/", UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0 || host.Any(char.IsWhiteSpace)) return null;
        return uri.IdnHost;
    }
    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_browser_mounts")]
    private static extern IntPtr NativeRead();
    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_storage_free")]
    private static extern void NativeFree(IntPtr pointer);
}
