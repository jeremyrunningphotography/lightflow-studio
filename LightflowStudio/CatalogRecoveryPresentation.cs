namespace LightflowStudio;

internal enum CatalogRecoveryReason { None, LocationUnavailable, CompetingOwner, PendingRecovery, ValidationFailed, OwnershipUnavailable, LocationRefused, UnsupportedStorage }

internal sealed record CatalogRecoveryPresentation(string Heading, string Description, string Diagnostic, bool ShowRecovery,
    bool NoReplacementCreated)
{
    internal static CatalogRecoveryPresentation Create(StorageStartupStatus status, CatalogRecoveryReason reason, string? diagnostic)
    {
        if (status == StorageStartupStatus.Ready) return new("", "", "", false, false);
        if (status == StorageStartupStatus.FirstRunChoiceRequired)
            return new("Welcome to Lightflow Studio", "Create a new Catalog or open an existing one to get started.", "", false, false);
        if (reason == CatalogRecoveryReason.None)
            reason = status is StorageStartupStatus.CatalogMissing or StorageStartupStatus.CatalogUnavailable
                ? CatalogRecoveryReason.LocationUnavailable
                : status == StorageStartupStatus.InvalidConfiguration ? CatalogRecoveryReason.OwnershipUnavailable : CatalogRecoveryReason.ValidationFailed;
        var (heading, description) = reason switch
        {
            CatalogRecoveryReason.CompetingOwner => ("Catalog already in use", "This Catalog is open in another Lightflow Studio session. Close it there, then try again."),
            CatalogRecoveryReason.PendingRecovery => ("Catalog needs attention", "Lightflow Studio couldn't finish a previous Catalog operation. Your Catalog files have been preserved."),
            CatalogRecoveryReason.UnsupportedStorage => ("Catalog location not supported", "Lightflow Studio can't open a Catalog from this type of storage. Choose a supported location."),
            CatalogRecoveryReason.ValidationFailed => ("Couldn't open Catalog", status == StorageStartupStatus.CatalogCorrupt
                ? "This Catalog is damaged and could not be opened. Your Catalog files have been preserved."
                : "The selected location doesn't contain a valid Lightflow Studio Catalog."),
            CatalogRecoveryReason.LocationUnavailable => ("Catalog unavailable", "Lightflow Studio couldn't find your last Catalog. Reconnect its drive or choose another Catalog."),
            _ => ("Couldn't open Catalog", "Lightflow Studio couldn't open this Catalog. Try again or choose another Catalog.")
        };
        var noReplacement = status is StorageStartupStatus.CatalogUnavailable or StorageStartupStatus.CatalogMissing
            or StorageStartupStatus.CatalogUnreadable or StorageStartupStatus.CatalogCorrupt or StorageStartupStatus.CatalogIdentityMismatch;
        return new(heading, description, diagnostic ?? "", true, noReplacement);
    }
}
