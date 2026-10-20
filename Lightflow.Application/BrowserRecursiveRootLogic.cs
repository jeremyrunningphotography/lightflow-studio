namespace LightflowStudio;

public static class BrowserRecursiveRootLogic
{
    /// <summary>True when <paramref name="relativeFolder"/> is governed by any stored root for <paramref name="rootId"/> — equal to it or a descendant.</summary>
    public static bool IsEffectivelyRecursive(IReadOnlyList<BrowserRecursiveRoot> roots, Guid rootId, string relativeFolder) =>
        GoverningRoots(roots, rootId, relativeFolder).Count > 0;

    /// <summary>
    /// The stored root(s) — normally at most one in a correctly normalized table — whose scope already covers
    /// <paramref name="relativeFolder"/>. Used both to answer "is this folder effectively recursive" and to
    /// find what a "disable" action at a descendant folder must remove.
    /// </summary>
    public static IReadOnlyList<BrowserRecursiveRoot> GoverningRoots(
        IReadOnlyList<BrowserRecursiveRoot> roots, Guid rootId, string relativeFolder) =>
        [.. roots.Where(root => root.RootId == rootId && BrowserScope.IsWithinFolderScope(relativeFolder, root.RelativeFolder))];

    /// <summary>
    /// Existing stored roots for <paramref name="rootId"/> that become redundant once <paramref name="relativeFolder"/>
    /// is established as a recursive root — its own strict-or-equal descendants. Normalization rule 2: an
    /// ancestor that newly becomes a recursive root absorbs any descendant roots it now already covers.
    /// </summary>
    public static IReadOnlyList<BrowserRecursiveRoot> RedundantDescendants(
        IReadOnlyList<BrowserRecursiveRoot> roots, Guid rootId, string relativeFolder) =>
        [.. roots.Where(root => root.RootId == rootId && BrowserScope.IsWithinFolderScope(root.RelativeFolder, relativeFolder))];
}

