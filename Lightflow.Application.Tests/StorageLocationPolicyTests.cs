using Lightflow.Domain;
using Xunit;

namespace Lightflow.Application.Tests;

public sealed class StorageLocationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static StorageAssessmentRequest Request(StorageRole role = StorageRole.ActiveCatalog,
        StorageOperation operation = StorageOperation.Open, string location = "opaque://local/catalog") =>
        new(Guid.NewGuid(), 1, role, operation, location);
    private static StorageLocationAssessment Facts(StorageAssessmentRequest request) => new(request, Guid.NewGuid(),
        Now.AddSeconds(-1), Now.AddMinutes(1), StorageLocationPolicy.Version, StorageAssessmentStatus.Complete,
        new("resolved://catalog", "target-1", "volume-1/mount-1", "filesystem-1"), StorageLocality.Local,
        StorageAvailability.Available, StorageAvailability.Available, StorageResolutionConfidence.Resolved,
        StorageResolutionConfidence.Resolved, new(StorageCapability.Supported, StorageCapability.Supported,
            StorageCapability.Supported, StorageCapability.Supported, StorageCapability.Supported, StorageCapability.Supported));
    private static StorageLocationPolicyResult Evaluate(StorageLocationAssessment facts) => StorageLocationPolicy.Evaluate(facts.Request, facts, Now);
    private static void Reason(StorageLocationReason expected, StorageLocationAssessment facts)
    {
        var result = Evaluate(facts);
        Assert.Equal(expected, result.Reason);
        Assert.Equal(StorageLocationDecision.Rejected, result.Decision);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
        Assert.Same(facts, result.Assessment);
        Assert.Equal(StorageLocationPolicy.Version, result.PolicyVersion);
    }

    [Theory]
    [InlineData(StorageRole.ActiveCatalog, StorageOperation.Open, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.ActiveCatalog, StorageOperation.Create, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.ActiveCatalog, StorageOperation.Relocate, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.ActiveCatalog, StorageOperation.RestoreActivation, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.ClosedCatalogBackupTransfer, StorageOperation.Read, StorageLocationDecision.RequiresVerifiedStaging)]
    [InlineData(StorageRole.ClosedCatalogBackupTransfer, StorageOperation.Write, StorageLocationDecision.RequiresVerifiedStaging)]
    [InlineData(StorageRole.MediaSource, StorageOperation.Read, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.RebuildablePreview, StorageOperation.Read, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.RebuildablePreview, StorageOperation.Write, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.RebuildablePreview, StorageOperation.Relocate, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.TemporaryProfile, StorageOperation.Read, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.TemporaryProfile, StorageOperation.Write, StorageLocationDecision.Eligible)]
    public void EveryRoleAndSupportedOperation(StorageRole role, StorageOperation operation, StorageLocationDecision decision)
    {
        Assert.Equal(decision, Evaluate(Facts(Request(role, operation))).Decision);
    }

    [Theory]
    [InlineData("internal-volume")]
    [InlineData("external-usb-volume")]
    [InlineData("external-thunderbolt-volume")]
    public void QualifiedLocalInternalAndExternalVolumesAreEquivalent(string volume)
    {
        var facts = Facts(Request());
        Assert.Equal(StorageLocationDecision.Eligible, Evaluate(facts with { Identity = facts.Identity! with { VolumeIdentity = volume } }).Decision);
    }

    [Theory]
    [InlineData(@"\\nas\catalog")]
    [InlineData(@"Z:\Catalog")]
    [InlineData(@"C:\AliasToSmb\Catalog")]
    [InlineData("/Volumes/share/catalog")]
    public void NetworkFactsRejectActiveCatalogRegardlessOfPathSpelling(string path)
    {
        Reason(StorageLocationReason.NetworkActiveCatalog, Facts(Request(location: path)) with { Locality = StorageLocality.Network });
    }

    [Fact]
    public void NoPathSpellingHeuristicsOverrideResolvedFacts()
    {
        Assert.Equal(StorageLocationDecision.Eligible, Evaluate(Facts(Request(location: "//opaque-adapter-identity"))).Decision);
    }

    [Theory]
    [InlineData(StorageRole.MediaSource, StorageOperation.Read, StorageLocationDecision.Eligible)]
    [InlineData(StorageRole.ClosedCatalogBackupTransfer, StorageOperation.Write, StorageLocationDecision.RequiresVerifiedStaging)]
    [InlineData(StorageRole.ClosedCatalogBackupTransfer, StorageOperation.Read, StorageLocationDecision.RequiresVerifiedStaging)]
    [InlineData(StorageRole.TemporaryProfile, StorageOperation.Write, StorageLocationDecision.Eligible)]
    public void NetworkByteRolesDoNotInheritCatalogDurability(StorageRole role, StorageOperation operation, StorageLocationDecision decision)
    {
        var facts = Facts(Request(role, operation));
        facts = facts with { Locality = StorageLocality.Network, Capabilities = facts.Capabilities! with
        {
            QualifiedCatalogFileSystem = StorageCapability.Unsupported, DurableWrites = StorageCapability.Unknown,
            FileLocking = StorageCapability.Unsupported, CompanionFiles = StorageCapability.Unknown
        } };
        var result = Evaluate(facts);
        Assert.Equal(decision, result.Decision);
        if (role == StorageRole.ClosedCatalogBackupTransfer)
        {
            Assert.Equal(StorageLocationReason.VerifiedStagingRequired, result.Reason);
            Assert.Contains("local copyback", result.Diagnostic);
        }
    }

    [Fact]
    public void PreviewIsLocalRebuildableAndDoesNotRequireCatalogCapabilities()
    {
        var facts = Facts(Request(StorageRole.RebuildablePreview, StorageOperation.Write));
        facts = facts with { Capabilities = facts.Capabilities! with { QualifiedCatalogFileSystem = StorageCapability.Unsupported,
            DurableWrites = StorageCapability.Unsupported, FileLocking = StorageCapability.Unknown, CompanionFiles = StorageCapability.Unknown } };
        Assert.Equal(StorageLocationDecision.Eligible, Evaluate(facts).Decision);
        Reason(StorageLocationReason.NetworkPreview, facts with { Locality = StorageLocality.Network });
    }

    [Theory]
    [InlineData(StorageLocality.Unknown, StorageLocationReason.UnknownLocality)]
    [InlineData(StorageLocality.Unavailable, StorageLocationReason.LocationUnavailable)]
    [InlineData((StorageLocality)999, StorageLocationReason.UnknownLocality)]
    public void UnknownAndUnavailableLocalityFailClosed(StorageLocality locality, StorageLocationReason reason) =>
        Reason(reason, Facts(Request()) with { Locality = locality });

    [Fact]
    public void UnavailableAndUnknownVolumeOrPathFailClosed()
    {
        var facts = Facts(Request());
        Reason(StorageLocationReason.LocationUnavailable, facts with { VolumeAvailability = StorageAvailability.Unavailable });
        Reason(StorageLocationReason.LocationUnavailable, facts with { PathAvailability = StorageAvailability.Unavailable });
        Reason(StorageLocationReason.IncompleteAssessment, facts with { VolumeAvailability = StorageAvailability.Unknown });
        Reason(StorageLocationReason.IncompleteAssessment, facts with { PathAvailability = StorageAvailability.Unknown });
    }

    [Fact]
    public void ReadOnlyMediaAndClosedSourcesRemainReadableButAuthoringDestinationsFail()
    {
        foreach (var role in new[] { StorageRole.ActiveCatalog, StorageRole.RebuildablePreview, StorageRole.TemporaryProfile, StorageRole.ClosedCatalogBackupTransfer })
        {
            var operation = role == StorageRole.ActiveCatalog ? StorageOperation.Open : StorageOperation.Write;
            var facts = Facts(Request(role, operation));
            Reason(StorageLocationReason.ReadOnly, facts with { Capabilities = facts.Capabilities! with { Write = StorageCapability.Unsupported } });
        }
        foreach (var role in new[] { StorageRole.MediaSource, StorageRole.ClosedCatalogBackupTransfer })
        {
            var facts = Facts(Request(role, StorageOperation.Read));
            Assert.NotEqual(StorageLocationDecision.Rejected, Evaluate(facts with { Capabilities = facts.Capabilities! with { Write = StorageCapability.Unsupported } }).Decision);
            Reason(StorageLocationReason.ReadUnavailable, facts with { Capabilities = facts.Capabilities! with { Read = StorageCapability.Unsupported } });
        }
    }

    [Theory]
    [InlineData(StorageCapability.Unknown, StorageLocationReason.MissingCapabilities)]
    [InlineData(StorageCapability.Unsupported, StorageLocationReason.UnsupportedCatalogFileSystem)]
    public void ActiveCatalogRequiresQualifiedFilesystem(StorageCapability qualification, StorageLocationReason reason)
    {
        var facts = Facts(Request());
        Reason(reason, facts with { Capabilities = facts.Capabilities! with { QualifiedCatalogFileSystem = qualification } });
    }

    [Theory]
    [InlineData(StorageCapability.Unknown, StorageLocationReason.MissingCapabilities)]
    [InlineData(StorageCapability.Unsupported, StorageLocationReason.UnsupportedCatalogCapabilities)]
    public void EveryCatalogSafetyCapabilityIsRequired(StorageCapability capability, StorageLocationReason reason)
    {
        var facts = Facts(Request());
        Reason(reason, facts with { Capabilities = facts.Capabilities! with { DurableWrites = capability } });
        Reason(reason, facts with { Capabilities = facts.Capabilities! with { FileLocking = capability } });
        Reason(reason, facts with { Capabilities = facts.Capabilities! with { CompanionFiles = capability } });
    }

    [Fact]
    public void MissingFactsAndMalformedAssessmentsFailClosed()
    {
        var facts = Facts(Request());
        Reason(StorageLocationReason.MissingCapabilities, facts with { Capabilities = null });
        Reason(StorageLocationReason.MissingCapabilities, facts with { Capabilities = facts.Capabilities! with { Write = StorageCapability.Unknown } });
        Reason(StorageLocationReason.MissingCapabilities, facts with { Capabilities = facts.Capabilities! with { Read = StorageCapability.Unknown } });
        Reason(StorageLocationReason.IncompleteAssessment, facts with { AssessmentId = Guid.Empty });
        Reason(StorageLocationReason.IncompleteAssessment, facts with { Status = StorageAssessmentStatus.Unknown });
        Assert.Equal(StorageLocationReason.IncompleteAssessment, StorageLocationPolicy.Evaluate(facts.Request, null, Now).Reason);
        Reason(StorageLocationReason.MissingIdentity, facts with { Identity = null });
        foreach (var identity in new[] { facts.Identity! with { CanonicalLocation = "" }, facts.Identity! with { LocationIdentity = "" },
            facts.Identity! with { VolumeIdentity = "" }, facts.Identity! with { FileSystemIdentity = "" } })
            Reason(StorageLocationReason.MissingIdentity, facts with { Identity = identity });
    }

    [Theory]
    [InlineData(StorageResolutionConfidence.Unknown)]
    [InlineData(StorageResolutionConfidence.Ambiguous)]
    public void UnresolvedAliasesAndContainmentFailClosed(StorageResolutionConfidence confidence)
    {
        var facts = Facts(Request());
        Reason(StorageLocationReason.UnresolvedAliases, facts with { AliasResolution = confidence });
        Reason(StorageLocationReason.AmbiguousContainment, facts with { Containment = confidence });
    }

    [Fact]
    public void SnapshotLifetimeAndVersionAreEnforced()
    {
        var facts = Facts(Request());
        Reason(StorageLocationReason.StaleAssessment, facts with { ExpiresAtUtc = Now });
        Reason(StorageLocationReason.StaleAssessment, facts with { AssessedAtUtc = Now.AddSeconds(1) });
        Reason(StorageLocationReason.StaleAssessment, facts with { ExpiresAtUtc = facts.AssessedAtUtc });
        Reason(StorageLocationReason.PolicyVersionMismatch, facts with { PolicyVersion = "old-policy" });
    }

    [Fact]
    public void OperationRolePathAndGenerationCannotBorrowAnAssessment()
    {
        var facts = Facts(Request());
        foreach (var changed in new[] { facts.Request with { OperationId = Guid.NewGuid() }, facts.Request with { Generation = 2 },
            facts.Request with { RequestedLocation = "different" }, facts.Request with { Operation = StorageOperation.Create },
            facts.Request with { Role = StorageRole.MediaSource, Operation = StorageOperation.Read } })
            Assert.Equal(StorageLocationReason.RequestMismatch, StorageLocationPolicy.Evaluate(changed, facts, Now).Reason);
    }

    [Fact]
    public void InvalidRequestsAndRoleOperationPairsFailClosed()
    {
        foreach (var request in new[] { Request() with { OperationId = Guid.Empty }, Request() with { Generation = -1 },
            Request() with { RequestedLocation = " " }, Request(StorageRole.Unknown), Request(operation: StorageOperation.Read),
            Request(StorageRole.MediaSource, StorageOperation.Write), Request(StorageRole.ClosedCatalogBackupTransfer, StorageOperation.Open),
            Request(StorageRole.TemporaryProfile, StorageOperation.RestoreActivation) })
            Assert.Equal(StorageLocationReason.InvalidRequest, StorageLocationPolicy.Evaluate(request, Facts(request), Now).Reason);
    }

    [Fact]
    public void RevalidationRequiresNewSnapshotAndDetectsEveryIdentityAndFactChange()
    {
        var previous = Facts(Request());
        var current = previous with { AssessmentId = Guid.NewGuid(), AssessedAtUtc = Now };
        Assert.Equal(StorageLocationDecision.Eligible, StorageLocationPolicy.Revalidate(previous.Request, previous, current, Now).Decision);
        void Check(StorageLocationReason reason, StorageLocationAssessment changed) =>
            Assert.Equal(reason, StorageLocationPolicy.Revalidate(previous.Request, previous, changed, Now).Reason);
        Check(StorageLocationReason.StaleAssessment, previous);
        Check(StorageLocationReason.StaleAssessment, current with { AssessedAtUtc = previous.AssessedAtUtc.AddTicks(-1) });
        Check(StorageLocationReason.VolumeChanged, current with { Identity = current.Identity! with { VolumeIdentity = "replacement/mount-2" } });
        Check(StorageLocationReason.FileSystemChanged, current with { Identity = current.Identity! with { FileSystemIdentity = "new-fs" } });
        Check(StorageLocationReason.LocationChanged, current with { Identity = current.Identity! with { LocationIdentity = "new-target" } });
        Check(StorageLocationReason.LocationChanged, current with { Identity = current.Identity! with { CanonicalLocation = "new-canonical" } });
        Check(StorageLocationReason.NetworkActiveCatalog, current with { Locality = StorageLocality.Network });
        Check(StorageLocationReason.ReadOnly, current with { Capabilities = current.Capabilities! with { Write = StorageCapability.Unsupported } });
        var media = Facts(Request(StorageRole.MediaSource, StorageOperation.Read));
        Assert.Equal(StorageLocationReason.CapabilityFactsChanged, StorageLocationPolicy.Revalidate(media.Request, media,
            media with { AssessmentId = Guid.NewGuid(), Capabilities = media.Capabilities! with { Write = StorageCapability.Unsupported } }, Now).Reason);
        Assert.Equal(StorageLocationReason.PolicyVersionMismatch, StorageLocationPolicy.Revalidate(previous.Request,
            previous with { PolicyVersion = "old-policy" }, current, Now).Reason);
        Assert.Equal(StorageLocationReason.RequestMismatch, StorageLocationPolicy.Revalidate(previous.Request,
            previous with { Request = previous.Request with { Generation = 0 } }, current, Now).Reason);
        Assert.Equal(StorageLocationReason.IncompleteAssessment, StorageLocationPolicy.Revalidate(previous.Request,
            previous with { AssessmentId = Guid.Empty }, current, Now).Reason);
        Assert.Equal(StorageLocationReason.IncompleteAssessment, StorageLocationPolicy.Revalidate(previous.Request,
            previous with { Identity = null }, current, Now).Reason);
        // An expired prior snapshot can be refreshed; its old approval never authorizes this boundary.
        Assert.Equal(StorageLocationDecision.Eligible, StorageLocationPolicy.Revalidate(previous.Request,
            previous with { ExpiresAtUtc = Now.AddTicks(-1) }, current, Now).Decision);
    }

    [Theory]
    [InlineData(StorageRole.ActiveCatalog, StorageOperation.Create)]
    [InlineData(StorageRole.MediaSource, StorageOperation.Read)]
    [InlineData(StorageRole.ClosedCatalogBackupTransfer, StorageOperation.Write)]
    [InlineData(StorageRole.RebuildablePreview, StorageOperation.Write)]
    [InlineData(StorageRole.TemporaryProfile, StorageOperation.Write)]
    public void CommonSafetyFactsApplyToEachRole(StorageRole role, StorageOperation operation)
    {
        var facts = Facts(Request(role, operation));
        Reason(StorageLocationReason.UnknownLocality, facts with { Locality = StorageLocality.Unknown });
        Reason(StorageLocationReason.LocationUnavailable, facts with { PathAvailability = StorageAvailability.Unavailable });
        Reason(StorageLocationReason.UnresolvedAliases, facts with { AliasResolution = StorageResolutionConfidence.Ambiguous });
        Reason(StorageLocationReason.AmbiguousContainment, facts with { Containment = StorageResolutionConfidence.Unknown });
        Reason(StorageLocationReason.StaleAssessment, facts with { ExpiresAtUtc = Now });
        Reason(StorageLocationReason.PolicyVersionMismatch, facts with { PolicyVersion = "old" });
    }

    [Fact]
    public void FailureAndCancellationNeverProduceEligibility()
    {
        var facts = Facts(Request());
        Reason(StorageLocationReason.AssessmentFailed, facts with { Status = StorageAssessmentStatus.Failed, ProviderDiagnostic = "volume probe failed" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = StorageLocationPolicy.Evaluate(facts.Request, facts, Now, cancellation.Token);
        Assert.Equal(StorageLocationDecision.Cancelled, cancelled.Decision);
        Assert.Equal(StorageLocationReason.Cancelled, cancelled.Reason);
        Assert.Equal(StorageLocationDecision.Cancelled, Evaluate(facts with { Status = StorageAssessmentStatus.Cancelled }).Decision);
        Assert.Equal(StorageLocationDecision.Cancelled, StorageLocationPolicy.Revalidate(facts.Request, facts,
            facts with { AssessmentId = Guid.NewGuid() }, Now, cancellation.Token).Decision);
    }

    [Fact]
    public void DeterministicPrecedenceAndNoMutationOrCatalogCreation()
    {
        // A path that does not exist is merely opaque input: policy cannot probe or create it.
        var path = Path.Combine(Path.GetTempPath(), "LF-WIN-DEV-004-" + Guid.NewGuid().ToString("N"), "Catalog");
        var facts = Facts(Request(location: path)) with { Locality = StorageLocality.Network, AliasResolution = StorageResolutionConfidence.Ambiguous };
        var before = facts with { };
        var first = Evaluate(facts);
        Assert.Equal(first, Evaluate(facts));
        Assert.Equal(StorageLocationReason.NetworkActiveCatalog, first.Reason);
        Assert.Equal(before, facts);
        Assert.Equal(path, first.Request.RequestedLocation);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.False(File.Exists(Path.Combine(path, "LightflowCatalog.db")));
        Assert.Equal(StorageLocationDecision.Eligible, Evaluate(facts with { Locality = StorageLocality.Local,
            AliasResolution = StorageResolutionConfidence.Resolved }).Decision);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void NeutralAssemblyClosureExcludesShellFrameworkNativeAndSqlite()
    {
        var assemblies = new[] { typeof(StorageLocationPolicy).Assembly, typeof(StorageLocationAssessment).Assembly,
            typeof(Lightflow.Actions.ActionArguments).Assembly };
        foreach (var assembly in assemblies)
        {
            Assert.All(assembly.GetReferencedAssemblies(), reference =>
                Assert.DoesNotContain(new[] { "LightflowStudio", "PresentationFramework", "PresentationCore", "WindowsBase",
                    "System.Windows.Forms", "Microsoft.Data.Sqlite", "SQLitePCLRaw.core" }, forbidden => reference.Name == forbidden));
            Assert.All(assembly.GetTypes().SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)),
                method => Assert.False((method.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0));
        }
        Assert.All(typeof(StorageLocationAssessment).GetProperties(), property =>
            Assert.DoesNotContain(property.PropertyType, new[] { typeof(IntPtr), typeof(UIntPtr) }));
    }
}
