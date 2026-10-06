namespace LightflowStudio;

// Compile-only name used by ApplicationDataProfile.InstanceIdentity; no instance coordinator runs.
internal static class WindowsApplicationInstanceCoordinator
{
    internal const string ApplicationIdentity = "LightflowStudio.X2Proof.UnusedInstanceIdentity";
}

// Preview persistence only consumes this constant. Scheduler/thumbnail code is excluded.
// Exact value from production PreviewRetryPolicy; this does not qualify retry scheduling.
internal static class PreviewRetryPolicy
{
    internal static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);
}

// Recursive scope service is excluded; only its real SQL repository is called.
internal static class BrowserScope
{
    internal static bool IsWithinFolderScope(string? candidate, string basis) =>
        throw new NotSupportedException("Browser scope evaluation is outside this persistence harness.");
}
