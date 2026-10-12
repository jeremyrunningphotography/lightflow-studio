namespace LightflowStudio;
internal sealed class ClosedCatalogTransferJournal : CatalogTransferJournal
{
    internal ClosedCatalogTransferJournal(LightflowStorageLocations locations, Guid operationId)
        : base(WindowsCatalogTransferEffects.Instance, locations, operationId) { }
    internal static string DirectoryPath(LightflowStorageLocations locations) => CatalogTransferJournal.DirectoryPath(WindowsCatalogTransferEffects.Instance, locations);
    internal static IReadOnlyList<Lightflow.Application.ClosedCatalogJournalRecord> Read(string path) => CatalogTransferJournal.Read(WindowsCatalogTransferEffects.Instance, path);
    internal static string? InspectPending(LightflowStorageLocations locations) => CatalogTransferJournal.InspectPending(WindowsCatalogTransferEffects.Instance, locations);
    internal static void RequireUnlinked(string path) => WindowsCatalogTransferEffects.Instance.RequireUnlinked(path);
}
