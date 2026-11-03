using Lightflow.Application;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

/// <summary>Native location discovery only. No Catalog, probe, mapping or ingestion effects.</summary>
public sealed class MacBrowserLocationProvider : IBrowserLocationProvider
{
    private readonly SemaphoreSlim _enumeration = new(2, 2), _navigation = new(2, 2), _mounts = new(1, 1);
    public async Task<IReadOnlyList<BrowserFileLocation>> GetLocationsAsync(CancellationToken token)
    {
        await _mounts.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // An OS metadata call cannot be forcibly interrupted. Keep at most one
            // actual snapshot in flight; newer cancelled hints do not accumulate threads.
            return await Task.Run<IReadOnlyList<BrowserFileLocation>>(() =>
            {
                token.ThrowIfCancellationRequested();
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var mounts = MacBrowserMounts.Read();
                token.ThrowIfCancellationRequested();
                return MacBrowserMounts.Locations(home, mounts);
            },token).ConfigureAwait(false);
        }
        finally { _mounts.Release(); }
    }

    public async Task<BrowserLocationListing> GetFoldersAsync(string absolutePath, CancellationToken token, bool userInitiated = false)
    {
        var lane = userInitiated ? _navigation : _enumeration;
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var paths = new MacBrowserMediaEffects([]);
            var path = paths.NormalizeRootPath(absolutePath);
            MacBrowserMediaEffects.RefuseApplicationPackage(path);
            // Start at / so every component, including the requested folder, is checked for links.
            var entries = await new ContainedMediaFolderFileSystem(paths, directoriesOnly: true).EnumerateAsync("/", path, token).ConfigureAwait(false);
            return new(MediaFolderEnumerationStatus.Succeeded, entries
                .Where(entry => entry.IsDirectory && !entry.IsReparsePoint && !entry.Name.StartsWith('.') &&
                    !MacBrowserMediaEffects.IsApplicationPackagePath(entry.FullPath))
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
