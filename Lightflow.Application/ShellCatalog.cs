using LightflowStudio;
namespace Lightflow.Application;

/// <summary>A presentation projection of the existing Catalog readiness result, never a persistence state.</summary>
public sealed record ShellCatalogSnapshot(StorageStartupStatus Status, CatalogRecoveryReason Reason = CatalogRecoveryReason.None,
    string? Diagnostic = null, string? Directory = null, Guid? CatalogId = null, int? SchemaVersion = null)
{
    public bool IsReady => Status == StorageStartupStatus.Ready;
}

/// <summary>Application-facing Catalog requests. Implementations delegate to the sole Catalog lifecycle.</summary>
public interface IShellCatalogSession : IAsyncDisposable
{
    string DataRoot { get; }
    Task<ShellCatalogSnapshot> StartAsync();
    Task<ShellCatalogSnapshot> CreateAsync(string preparedDirectory);
    Task<ShellCatalogSnapshot> OpenAsync(string directory);
    Task<ShellCatalogSnapshot> VerifyAsync();
}
