using System.IO;

namespace LightflowStudio;

internal enum BrowserRestorationOutcome { NoSavedLocation, RootNotFound, RootUnavailable, Restored, Unresolvable }

internal sealed record BrowserRestorationResult(BrowserRestorationOutcome Outcome, BrowserFolderState? State = null);

/// <summary>
/// Restores a persisted root-agnostic Browser location by driving the existing #98-#108
/// <see cref="BrowserNavigationSession"/> exactly as ordinary Locations interaction would (root click,
/// direct-path entry). This is deliberately not a parallel enumeration/reconciliation path: every filesystem
/// or Catalog interaction happens through <see cref="BrowserNavigationSession.NavigateToRootAsync"/> or
/// <see cref="BrowserNavigationSession.NavigateToPathAsync"/>.
/// </summary>
internal static class BrowserLocationRestoration
{
    /// <summary>
    /// The saved relative folder, then each of its ancestors from most to least specific, ending with the
    /// Media Root itself (""). Restoration tries these in order and stops at the first one that resolves,
    /// giving a deleted/renamed folder graceful fallback to its nearest still-valid ancestor.
    /// </summary>
    internal static IReadOnlyList<string> AncestorRelativeFolderCandidates(string relativeFolder)
    {
        if (relativeFolder.Length == 0) return [""];
        string normalized;
        try { normalized = MediaPathSemantics.NormalizeRelativePath(relativeFolder); }
        catch (ArgumentException) { return [""]; }

        var segments = normalized.Split('/');
        var candidates = new List<string>(segments.Length + 1);
        for (var count = segments.Length; count > 0; count--)
            candidates.Add(string.Join('/', segments.Take(count)));
        candidates.Add("");
        return candidates;
    }

    /// <summary>
    /// A short, human-readable label for the saved location, safe to show before any restoration work has
    /// run: it uses only synchronously-available data (the saved relative folder's last segment, falling
    /// back to the diagnostic-only last-resolved path's file name). Never performs I/O or Catalog access.
    /// Returns null when no reasonable label is available.
    /// </summary>
    internal static string? DescribeSavedLocation(WorkspaceBrowserLocationState saved)
    {
        if (saved.RelativeFolder.Length > 0)
        {
            var lastSegment = saved.RelativeFolder.Split('/')[^1];
            if (lastSegment.Length > 0) return lastSegment;
        }
        if (string.IsNullOrWhiteSpace(saved.LastResolvedAbsolutePath)) return null;
        var trimmed = saved.LastResolvedAbsolutePath.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? saved.LastResolvedAbsolutePath : name;
    }

    public static async Task<BrowserRestorationResult> RestoreAsync(BrowserNavigationSession session,
        IMediaRootService roots, WorkspaceBrowserLocationState? saved, CancellationToken cancellationToken = default)
    {
        if (saved is null) return new(BrowserRestorationOutcome.NoSavedLocation);

        MediaRootInfo? root;
        try { root = await roots.GetAsync(saved.RootId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidOperationException or MachineIdentityException)
        {
            return new(BrowserRestorationOutcome.RootNotFound);
        }
        if (root is null) return new(BrowserRestorationOutcome.RootNotFound);

        if (root.Availability != MediaRootAvailability.Online || root.PhysicalPath is null)
        {
            // Preserve and restore this location's context: the same honest unavailable state a manual click
            // on this (offline) root in Locations would show, rather than silently falling back to the
            // default empty Browser view.
            var unavailable = await session.NavigateToRootAsync(saved.RootId, cancellationToken).ConfigureAwait(false);
            return new(BrowserRestorationOutcome.RootUnavailable, unavailable);
        }

        foreach (var candidate in AncestorRelativeFolderCandidates(saved.RelativeFolder))
        {
            var absolute = candidate.Length == 0
                ? root.PhysicalPath
                : MediaPathSemantics.ResolveContained(root.PhysicalPath, candidate);
            var state = await session.NavigateToPathAsync(absolute, cancellationToken).ConfigureAwait(false);
            if (state is not null && state.Status is BrowserFolderStatus.Ready or BrowserFolderStatus.Empty)
                return new(BrowserRestorationOutcome.Restored, state);
        }
        return new(BrowserRestorationOutcome.Unresolvable);
    }
}
