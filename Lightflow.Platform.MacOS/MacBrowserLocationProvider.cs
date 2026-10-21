using Lightflow.Application;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

/// <summary>Native location discovery only. No Catalog, probe, mapping or ingestion effects.</summary>
public sealed class MacBrowserLocationProvider : IBrowserLocationProvider
{
    private readonly SemaphoreSlim _enumeration = new(2, 2), _navigation = new(2, 2);
    public Task<IReadOnlyList<BrowserFileLocation>> GetLocationsAsync(CancellationToken token) => Task.Run<IReadOnlyList<BrowserFileLocation>>(() =>
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = new List<BrowserFileLocation> { new(home, "Home") };
        foreach (var name in new[] { "Desktop", "Documents", "Downloads" })
            result.Add(new(Path.Combine(home, name), name, CanPrefetch: false));
        // Read the actual mount table, including the startup volume and already-mounted
        // authorized network shares. No network discovery, credentials or mounting.
        foreach (var drive in DriveInfo.GetDrives())
        {
            token.ThrowIfCancellationRequested();
            var path = Path.TrimEndingDirectorySeparator(drive.Name);
            if (path != "/" && !path.StartsWith("/Volumes/",StringComparison.Ordinal)) continue;
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint|FileAttributes.Hidden)) != 0 ||
                    path.Split('/').Any(part=>part.StartsWith('.'))) continue;
                var name = path == "/" ? drive.VolumeLabel : Path.GetFileName(path);
                result.Add(new(path,string.IsNullOrWhiteSpace(name) ? path : name,CanPrefetch:false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result.DistinctBy(location => location.Path, StringComparer.Ordinal).ToArray();
    }, token);

    public async Task<BrowserLocationListing> GetFoldersAsync(string absolutePath, CancellationToken token, bool userInitiated = false)
    {
        var lane = userInitiated ? _navigation : _enumeration;
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var paths = new MacBrowserMediaEffects([]);
            var path = paths.NormalizeRootPath(absolutePath);
            // Start at / so every component, including the requested folder, is checked for links.
            var entries = await new ContainedMediaFolderFileSystem(paths, directoriesOnly: true).EnumerateAsync("/", path, token).ConfigureAwait(false);
            return new(MediaFolderEnumerationStatus.Succeeded, entries
                .Where(entry => entry.IsDirectory && !entry.IsReparsePoint && !entry.Name.StartsWith('.'))
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(entry => new BrowserFileLocation(entry.FullPath, entry.Name, CanPrefetch: false)).ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return new(MediaFolderEnumerationStatus.AccessDenied, []); }
        catch (DirectoryNotFoundException) { return new(MediaFolderEnumerationStatus.FolderNotFound, []); }
        catch (FileNotFoundException) { return new(MediaFolderEnumerationStatus.FolderNotFound, []); }
        catch (MediaFolderLinkException) { return new(MediaFolderEnumerationStatus.LinkedPathRejected, []); }
        catch (IOException) { return new(MediaFolderEnumerationStatus.FolderUnavailable, []); }
        catch (ArgumentException) { return new(MediaFolderEnumerationStatus.InvalidPath, []); }
        finally { lane.Release(); }
    }
}
