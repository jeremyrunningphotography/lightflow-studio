using System.IO;

namespace LightflowStudio;

internal static class WorkspaceTreeRestoration
{
    /// <summary>Walk only prefixes of recorded branches, with one listing per required node. No navigation or reconciliation.</summary>
    internal static async Task RestoreAsync(BrowserTreeModel tree, IMediaRootService roots,
        IMediaFolderEnumerator folders, IReadOnlyList<WorkspaceBrowserLocationState> expanded,
        CancellationToken token, Action<Action> publish)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var saved in expanded.OrderBy(folder => folder.RelativeFolder.Count(c => c is '/' or '\\')))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var root = await roots.GetAsync(saved.RootId, token);
                token.ThrowIfCancellationRequested();
                if (root?.PhysicalPath is not { } rootPath || root.Availability != MediaRootAvailability.Online) continue;
                BrowserTreeNode? node = null;
                publish(() => node = tree.EnsureWorkspaceRoot(new(root.RootId, root.DisplayName, rootPath, "")));
                var target = saved.RelativeFolder.Replace('\\', '/');
                var segments = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
                // Include the expanded node itself, since its direct children are part of the saved view.
                for (var depth = 0; node is not null && depth <= segments.Length; depth++)
                {
                    token.ThrowIfCancellationRequested();
                    var relative = depth == 0 ? "" : string.Join('/', segments.Take(depth));
                    // A volume row may precede the logical root. Establish its chain through the existing model.
                    if (depth == 0 && !string.Equals(node.AbsolutePath?.TrimEnd('\\'), rootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        publish(() => tree.GetUnmaterializedAncestors(new(root.RootId, root.DisplayName, rootPath, "")));
                        node = tree.FindByPath(rootPath);
                        if (node is null) break;
                    }
                    var key = $"{root.RootId}:{relative}";
                    if (!node.IsMaterialized && visited.Add(key))
                    {
                        var listing = await folders.EnumerateAsync(new(root.RootId, relative.Length == 0 ? null : relative), token);
                        token.ThrowIfCancellationRequested();
                        if (!listing.Succeeded) break;
                        var materialized = node;
                        publish(() => tree.ApplyDirectoryListing(materialized, rootPath, listing.Entries));
                    }
                    if (depth == segments.Length) { var last = node; publish(() => last.IsExpanded = true); break; }
                    var next = MediaPathSemantics.ResolveContained(rootPath, string.Join('/', segments.Take(depth + 1)));
                    node = node.Children.FirstOrDefault(child => string.Equals(child.AbsolutePath, next, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { /* One unavailable branch does not prevent the others from restoring. */ }
        }
    }
}
