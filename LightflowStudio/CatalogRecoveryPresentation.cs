namespace LightflowStudio;

internal enum CatalogRecoveryReason { None, LocationUnavailable, CompetingOwner, PendingRecovery, ValidationFailed, OwnershipUnavailable, LocationRefused }

internal sealed record CatalogRecoveryPresentation(string Heading, string Description, string Diagnostic, bool ShowRecovery,
    bool NoReplacementCreated)
{
    internal static CatalogRecoveryPresentation Create(StorageStartupStatus status, CatalogRecoveryReason reason, string? diagnostic)
    {
        if (status == StorageStartupStatus.Ready) return new("", "", "", false, false);
        if (reason == CatalogRecoveryReason.None)
            reason = status switch
            {
                StorageStartupStatus.CatalogMissing or StorageStartupStatus.CatalogUnavailable => CatalogRecoveryReason.LocationUnavailable,
                _ => CatalogRecoveryReason.ValidationFailed
            };
        var (heading, description) = reason switch
        {
            CatalogRecoveryReason.CompetingOwner => ("Catalog is open in another Lightflow instance", "Close the other instance using this Catalog, then retry."),
            CatalogRecoveryReason.PendingRecovery => ("Catalog recovery needs attention", "An interrupted Catalog transfer needs review before this Catalog can open. Its recovery artifacts have been retained."),
            CatalogRecoveryReason.ValidationFailed => ("Catalog validation failed", "Lightflow could not validate this Catalog for use. Review the troubleshooting details or open another existing Catalog."),
            CatalogRecoveryReason.OwnershipUnavailable => ("Catalog ownership could not be established", "Lightflow could not safely acquire this Catalog. Review the troubleshooting details before retrying."),
            CatalogRecoveryReason.LocationRefused => ("Catalog location could not be accepted", "This location did not pass Lightflow's storage safety checks. Review the troubleshooting details or open a Catalog on supported local storage."),
            _ => ("Catalog location is unavailable", "Lightflow cannot access the configured Catalog. Reconnect its storage or check the location, then retry.")
        };
        // These startup refusal states never substitute a new Catalog. Configuration/first-run
        // failures do not establish that fact and must not make the same assurance.
        var noReplacement = status is StorageStartupStatus.CatalogUnavailable or StorageStartupStatus.CatalogMissing
            or StorageStartupStatus.CatalogUnreadable or StorageStartupStatus.CatalogCorrupt or StorageStartupStatus.CatalogIdentityMismatch;
        return new(heading, description, diagnostic ?? "", true, noReplacement);
    }
}
