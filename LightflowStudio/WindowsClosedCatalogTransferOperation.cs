using Lightflow.Application;
namespace LightflowStudio;
internal sealed class WindowsClosedCatalogTransferOperation : ClosedCatalogTransferOperation
{
    internal WindowsClosedCatalogTransferOperation(LightflowStorageLocations locations, string destination,
        string previousConfiguration, string intendedConfiguration,
        Func<CancellationToken, Task> quiesce,
        Func<ClosedCatalogTransferRequest, string, CancellationToken, Task<ClosedCatalogManifest>> snapshot,
        Func<CancellationToken, Task<ClosedCatalogTransferReceipt>>? activate = null,
        Func<CancellationToken, Task<ClosedCatalogTransferReceipt>>? commit = null,
        Func<CancellationToken, Task>? complete = null, Func<CancellationToken, Task<bool>>? rollback = null,
        string? sourceDirectory = null)
        : base(WindowsCatalogTransferEffects.Instance, locations, destination, previousConfiguration, intendedConfiguration,
            quiesce, snapshot, activate, commit, complete, rollback, sourceDirectory) { }
    private static CatalogTransferFiles Files => new(WindowsCatalogTransferEffects.Instance);
    internal static System.IO.FileStream OpenClosedRead(string path) => Files.OpenClosedRead(path);
    internal static IEnumerable<string> EnumerateRegularFiles(string directory, string? ownedJournal = null) => Files.EnumerateRegularFiles(directory, ownedJournal);
    internal static string Child(string directory, string relative) => Files.Child(directory, relative);
    internal static void ValidateDirectorySet(string directory, ClosedCatalogManifest manifest) => Files.ValidateDirectorySet(directory, manifest);
    internal static byte[] ReadBounded(string path) => Files.ReadBounded(path);
    internal static void WriteExclusive(string path, byte[] bytes) => Files.WriteExclusive(path, bytes);
    internal static void VerifyMetadata(string directory, ClosedCatalogManifest manifest, ClosedCatalogReadiness readiness) => Files.VerifyMetadata(directory, manifest, readiness);
    internal static void CreateExclusiveDirectory(string path) => WindowsCatalogTransferEffects.Instance.CreateExclusiveDirectory(path);
}
