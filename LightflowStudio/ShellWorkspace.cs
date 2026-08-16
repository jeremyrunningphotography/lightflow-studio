namespace LightflowStudio;

internal enum ShellWorkspace
{
    Browser = 0,
    Encoding = 1,
    MediaTools = 2,
    History = 3,
    PremiereHelper = 4,
    Settings = 5,
    About = 6
}

internal static class ShellWorkspaceSelection
{
    public static ShellWorkspace Default => ShellWorkspace.Browser;
    public static int Index(ShellWorkspace workspace) => (int)workspace;

    public static ShellWorkspace FromIndex(int index) => Enum.IsDefined(typeof(ShellWorkspace), index)
        ? (ShellWorkspace)index
        : Default;
}
