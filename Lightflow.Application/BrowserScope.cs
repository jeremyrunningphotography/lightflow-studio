namespace LightflowStudio;

public static class BrowserScope
{
    /// <summary>
    /// True when <paramref name="candidateRelativeFolder"/> falls within the recursive scope rooted at
    /// <paramref name="baseRelativeFolder"/> — equal to it, a descendant of it, or the base scope is the
    /// Media Root itself (in which case every folder is inside it). A null candidate represents an
    /// ambiguous/root-level monitoring event (e.g. a watcher reconnect or overflow, which #101 already
    /// queues with a null relative folder); treated as relevant, since watcher events are hints only and an
    /// authoritative refresh in that case is the safe default. Malformed folder text is likewise treated as
    /// relevant rather than silently ignored, for the same reason.
    /// </summary>
    public static bool IsWithinFolderScope(string? candidateRelativeFolder, string baseRelativeFolder)
    {
        if (candidateRelativeFolder is null || baseRelativeFolder.Length == 0) return true;
        // An empty string is this codebase's convention for "the Media Root folder itself" (see
        // BrowserLocation.RelativeFolder) — meaningfully different from null (ambiguous/ancestor event) and
        // never itself inside a non-root base scope, so it must be rejected explicitly rather than falling
        // through to NormalizeRelativePath, which rejects empty paths for an unrelated reason (enforcing
        // well-formed non-empty relative file paths) and would otherwise be caught by the defensive fallback below.
        if (candidateRelativeFolder.Length == 0) return false;
        string candidate, @base;
        try
        {
            candidate = CatalogRelativePaths.NormalizeRelativePath(candidateRelativeFolder);
            @base = CatalogRelativePaths.NormalizeRelativePath(baseRelativeFolder);
        }
        catch (ArgumentException) { return true; }
        return string.Equals(candidate, @base, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(@base + "/", StringComparison.OrdinalIgnoreCase);
    }
}

