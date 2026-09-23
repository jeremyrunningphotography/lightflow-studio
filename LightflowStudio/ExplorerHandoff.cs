using System.Diagnostics;
using System.IO;

namespace LightflowStudio;

/// <summary>One Windows shell boundary. Paths are Explorer data, never command-shell input.</summary>
internal static class ExplorerShell
{
    internal static ProcessStartInfo Request(string path, bool selectFile)
    {
        if (path.Contains('"')) throw new ArgumentException("A Windows path cannot contain quotes.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var request = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        if (selectFile)
            request.Arguments = $"/select,\"{fullPath}\"";
        else
            request.ArgumentList.Add(fullPath);
        return request;
    }

    internal static void Launch(ProcessStartInfo request)
    {
        using var process = Process.Start(request);
    }
}

/// <summary>Transient context snapshot; logical folder/media identities resolve against today's mapping.</summary>
internal sealed record ExplorerTarget(Guid? RootId, string? RelativePath, string? PhysicalFolder, bool SelectFile)
{
    internal static ExplorerTarget? Folder(BrowserTreeNode? node) => node is null || node.IsPlaceholder ? null
        : node.RootId is { } root ? new(root, node.RelativeFolder ?? "", null, false)
        : node.Storage?.RootId is { } storageRoot ? new(storageRoot, "", null, false)
        : node.AbsolutePath is { } path ? new(null, null, path, false) : null;

    internal static ExplorerTarget Media(BrowserGridTile tile) => new(tile.RootId, tile.RelativePath, null, true);
}

internal sealed class ExplorerHandoff(
    Func<Guid, Task<MediaRootInfo?>> resolveRoot,
    Action<ProcessStartInfo>? launch = null)
{
    internal async Task<string?> ResolveAsync(ExplorerTarget? target)
    {
        if (target is null) return null;
        var path = target.PhysicalFolder;
        if (target.RootId is { } root)
        {
            var mapping = await resolveRoot(root).ConfigureAwait(false);
            if (mapping is not { Availability: MediaRootAvailability.Online, PhysicalPath: { } rootPath }) return null;
            path = string.IsNullOrEmpty(target.RelativePath) ? MediaPathSemantics.NormalizeRootPath(rootPath)
                : MediaPathSemantics.ResolveContained(rootPath, target.RelativePath);
        }
        return await Task.Run(() => (target.SelectFile ? File.Exists(path) : Directory.Exists(path)) ? path : null)
            .ConfigureAwait(false);
    }

    internal async Task<bool> CanOpenAsync(ExplorerTarget? target)
    {
        try { return await ResolveAsync(target).ConfigureAwait(false) is not null; }
        catch (Exception) { return false; }
    }

    internal async Task OpenAsync(ExplorerTarget? target)
    {
        var path = await ResolveAsync(target).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The folder or file is unavailable. Connect its location or refresh the Browser and try again.");
        await Task.Run(() => (launch ?? ExplorerShell.Launch)(ExplorerShell.Request(path, target!.SelectFile)))
            .ConfigureAwait(false);
    }
}
