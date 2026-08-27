namespace LightflowStudio;

internal enum ShellDestination
{
    Home,
    CompatibilityExportReview,
    Jobs,
    Settings,
    About
}

internal static class ShellDestinationSelection
{
    public static ShellDestination Default => ShellDestination.Home;

    // The compatibility review retains the proven legacy controls without exposing their old workspace.
    public static int Index(ShellDestination destination) => destination switch
    {
        ShellDestination.Home => 0,
        ShellDestination.CompatibilityExportReview => 1,
        ShellDestination.Jobs => 2,
        ShellDestination.Settings => 3,
        ShellDestination.About => 4,
        _ => 0
    };

    public static ShellDestination FromIndex(int index) => index switch
    {
        0 => ShellDestination.Home,
        1 => ShellDestination.CompatibilityExportReview,
        2 => ShellDestination.Jobs,
        3 => ShellDestination.Settings,
        4 => ShellDestination.About,
        _ => Default
    };
}
