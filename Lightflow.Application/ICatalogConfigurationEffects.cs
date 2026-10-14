namespace LightflowStudio;

/// <summary>A lossless host-owned snapshot; Serialize returns the existing complete configuration representation.</summary>
public interface ICatalogConfigurationSnapshot
{
    string? CatalogDirectory { get; }
    string? PreviewsDirectory { get; }
    Guid? CatalogId { get; }
    string? CatalogBackupDirectory { get; }
    string Serialize();
}

public sealed record CatalogConfigurationUpdate(
    bool SetCatalogDirectory = false, string? CatalogDirectory = null,
    bool SetCatalogId = false, Guid? CatalogId = null,
    bool SetBackupDirectory = false, string? CatalogBackupDirectory = null);

/// <summary>Configuration effects only. The lifecycle decides capture, staging, commit, readback and rollback order.</summary>
public interface ICatalogConfigurationEffects
{
    bool TryLoad(out ICatalogConfigurationSnapshot snapshot, out string? diagnostic);
    ICatalogConfigurationSnapshot Stage(ICatalogConfigurationSnapshot previous, CatalogConfigurationUpdate update);
    ICatalogConfigurationSnapshot InitializeIsolatedDefaults(ICatalogConfigurationSnapshot previous, string profileRoot);
    void Save(ICatalogConfigurationSnapshot snapshot);
}
