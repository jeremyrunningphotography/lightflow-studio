using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogRecoveryIsolationTests
{
    [Theory]
    [InlineData("Outside selected Catalog")]
    [InlineData("profile-sibling")]
    public async Task ContainmentDiagnosticIdentifiesOutsideSelectionAndSafeDistinctCreationWorks(string outsideName)
    {
        var evidence = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-final-recovery-diagnostic", Guid.NewGuid().ToString("N")));
        var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(evidence, "profile")]);
        ApplicationDataProfile.Initialize(profile);
        var missing = Path.Combine(profile.ApplicationDataDirectory, "Missing previous Catalog");
        var priorId = Guid.NewGuid();
        AppSettingsStore.Save(profile.SettingsPath, new() { CatalogDirectory = missing, CatalogId = priorId, BackupCatalogOnClose = false }, isolated: true);
        var outside = Path.Combine(evidence, outsideName);
        var refused = await LightflowStorageCoordinator.CreateCatalogAsync(outside, profile);
        await File.WriteAllTextAsync(Path.Combine(evidence, "classified-containment-refusal.json"), JsonSerializer.Serialize(new { profile.ApplicationDataDirectory, Selected = outside, refused.Status, refused.RecoveryReason, refused.Diagnostic }));
        Assert.False(refused.IsReady);
        Assert.Equal(StorageStartupStatus.CatalogUnavailable, refused.Status);
        Assert.Equal(CatalogRecoveryReason.OutsideWorkspace, refused.RecoveryReason);
        Assert.Contains("Allowed workspace: " + profile.ApplicationDataDirectory, refused.Diagnostic);
        Assert.DoesNotContain("--data-root", CatalogRecoveryPresentation.Create(refused.Status, refused.RecoveryReason, refused.Diagnostic).Description);
        Assert.False(Directory.Exists(outside));
        Assert.False(File.Exists(profile.CatalogDatabasePath));
        var safe = Path.Combine(profile.ApplicationDataDirectory, "New Catalog");
        var created = await LightflowStorageCoordinator.CreateCatalogAsync(safe, profile);
        Assert.True(created.IsReady, created.Diagnostic);
        await using var storage = created.Coordinator!;
        Assert.Equal(safe, storage.Locations.CatalogDirectory);
        Assert.NotEqual(priorId, storage.CatalogSession.Identity.CatalogId);
        Assert.False(File.Exists(Path.Combine(missing, LightflowStorageLocations.CatalogFileName)));
        Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(storage.CatalogSession.DatabasePath, "Catalog", false));
    }
}
