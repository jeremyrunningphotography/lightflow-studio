using Lightflow.Application;
using Lightflow.Domain;
using Xunit;

namespace Lightflow.Application.Tests;

public sealed class PortablePathIdentityTests
{
    [Theory]
    [InlineData("Events/2026/clip.MP4")]
    [InlineData("café/猫.jpg")]
    [InlineData("a space/clip-01.mov")]
    public void SafePathsPreserveSpelling(string path)
    {
        Assert.True(PortablePathIdentityValidator.ValidateRelativePath(path).IsSafe);
        Assert.Equal(path, new PathIdentityCandidate(Guid.NewGuid(), Guid.NewGuid(), path).RelativePath);
    }

    [Theory]
    [InlineData("CON.mp4")]
    [InlineData("aux")]
    [InlineData("LPT¹.txt")]
    [InlineData("COM9.mov")]
    [InlineData("conout$.txt")]
    [InlineData("clip.mp4.")]
    [InlineData("clip.mp4 ")]
    [InlineData(" clip.mp4")]
    [InlineData("file\t.mp4")]
    [InlineData("file?.mp4")]
    [InlineData("folder\\file.mp4")]
    [InlineData("C:clip.mp4")]
    [InlineData("/Volumes/media/clip.mp4")]
    [InlineData("\\\\server\\share\\clip.mp4")]
    public void UnsupportedPortableNamesAreRefused(string path) => Assert.Equal(PathIdentityStatus.UnsupportedPortableName,
        PortablePathIdentityValidator.ValidateRelativePath(path).Status);

    [Theory]
    [InlineData("../clip.mp4")]
    [InlineData("folder/../clip.mp4")]
    [InlineData("folder/./clip.mp4")]
    [InlineData("folder//clip.mp4")]
    [InlineData("folder/")]
    public void TraversalAndLossyComponentsAreRefused(string path) => Assert.Equal(PathIdentityStatus.UnsafeContainment,
        PortablePathIdentityValidator.ValidateRelativePath(path).Status);

    [Theory]
    [InlineData("clip.mp4", "CLIP.mp4")]
    [InlineData("café.mp4", "cafe\u0301.mp4")]
    [InlineData("clip.mp4", "clip.mp4")]
    public void DuplicateComparisonCandidatesNeverMerge(string left, string right)
    {
        var root = Guid.NewGuid();
        PathIdentityCandidate[] rows = [new(root, Guid.NewGuid(), left), new(root, Guid.NewGuid(), right)];
        var before = rows.ToArray();
        Assert.Equal(PathIdentityStatus.AmbiguousMapping, PortablePathIdentityValidator.ValidateCandidates(rows).Status);
        Assert.Equal(before, rows);
    }

    [Fact]
    public void CaseSensitiveSourceStillRefusesInsensitiveDestinationCollision()
    {
        var root = Guid.NewGuid();
        Assert.False(PortablePathIdentityValidator.ValidateCandidates([
            new(root, Guid.NewGuid(), "photo.jpg"), new(root, Guid.NewGuid(), "PHOTO.jpg")]).IsSafe);
        Assert.False(PortablePathIdentityValidator.ValidateCandidates([
            new(root, Guid.NewGuid(), "Folder/a.jpg"), new(root, Guid.NewGuid(), "folder/b.jpg")]).IsSafe);
    }

    [Theory]
    [InlineData("clip.mp4", "CLIP.mp4")]
    [InlineData("café.mp4", "cafe\u0301.mp4")]
    public void HistoricalSpellingIsNotRewritten(string historical, string current)
    {
        var root = Guid.NewGuid(); var asset = Guid.NewGuid();
        Assert.Equal(PathIdentityStatus.AmbiguousMapping, PortablePathIdentityValidator.ValidateReconciliation(
            [new(root, asset, historical)], [new(root, Guid.Empty, current)]).Status);
        Assert.True(PortablePathIdentityValidator.ValidateReconciliation([new(root, asset, historical)], []).IsSafe);
        Assert.False(PortablePathIdentityValidator.ValidateReconciliation([new(root, asset, "Folder/a.mp4")],
            [new(root, Guid.Empty, "folder/b.mp4")]).IsSafe);
    }

    [Fact]
    public void CatalogIdIsIndependentOfMountSpelling()
    {
        var id = Guid.NewGuid();
        Assert.True(PortablePathIdentityValidator.ValidateCatalogIdentity(id, id).IsSafe);
        Assert.False(PortablePathIdentityValidator.ValidateCatalogIdentity(id, Guid.NewGuid()).IsSafe);
    }

    [Fact]
    public void CancellationAndMalformedUnicodeFailClosed()
    {
        var cancelled = new CancellationToken(true);
        Assert.Equal(PathIdentityStatus.Cancelled, PortablePathIdentityValidator.ValidateRelativePath("safe.mp4", cancelled).Status);
        Assert.Equal(PathIdentityStatus.Cancelled, PortablePathIdentityValidator.ValidateCandidates([], cancelled).Status);
        Assert.Equal(PathIdentityStatus.UnsupportedPortableName, PortablePathIdentityValidator.ValidateRelativePath("bad\ud800.mp4").Status);
    }

    [Fact]
    public void NativeFactsAreRequiredAndChangesInvalidateMapping()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new StorageAssessmentRequest(Guid.NewGuid(), 0, StorageRole.MediaSource, StorageOperation.Read, "opaque-root");
        var first = new StorageLocationAssessment(request, Guid.NewGuid(), now, now.AddSeconds(30), StorageLocationPolicy.Version,
            StorageAssessmentStatus.Complete, new("resolved-root", "object", "win-volume", "filesystem"), StorageLocality.Network,
            StorageAvailability.Available, StorageAvailability.Available, StorageResolutionConfidence.Resolved,
            StorageResolutionConfidence.Resolved, new(StorageCapability.Supported, StorageCapability.Unknown,
                StorageCapability.Unsupported, StorageCapability.Unknown, StorageCapability.Unknown, StorageCapability.Unknown));
        Assert.True(PortablePathIdentityValidator.ValidateNativeMapping(request, first, now).IsSafe); // Network media remains eligible.
        Assert.Equal(PathIdentityStatus.NativeEvidenceUnavailable, PortablePathIdentityValidator.ValidateNativeMapping(request, null, now).Status);
        var next = first with { AssessmentId = Guid.NewGuid(), Identity = first.Identity! with { VolumeIdentity = "mac-volume" } };
        Assert.Equal(PathIdentityStatus.NativeEvidenceUnavailable, PortablePathIdentityValidator.ValidateNativeMapping(request, next, now, first).Status);
        next = first with { AssessmentId = Guid.NewGuid(), Containment = StorageResolutionConfidence.Ambiguous };
        Assert.Equal(PathIdentityStatus.UnsafeContainment, PortablePathIdentityValidator.ValidateNativeMapping(request, next, now).Status);
    }

    [Fact]
    public void LogicalRootsAndNativeContainmentCannotBeGuessed()
    {
        var root = Guid.NewGuid();
        Assert.True(PortablePathIdentityValidator.ValidateRootMapping(root, root, "native-object", "native-object").IsSafe);
        Assert.Equal(PathIdentityStatus.AmbiguousMapping, PortablePathIdentityValidator.ValidateRootMapping(
            root, Guid.NewGuid(), "native-object", "native-object").Status);
        Assert.Equal(PathIdentityStatus.NativeEvidenceUnavailable, PortablePathIdentityValidator.ValidateRootMapping(root, root, null, "native-object").Status);
        Assert.Equal(PathIdentityStatus.NativeEvidenceUnavailable, PortablePathIdentityValidator.ValidateContainment(StorageResolutionConfidence.Unknown).Status);
        Assert.Equal(PathIdentityStatus.UnsafeContainment, PortablePathIdentityValidator.ValidateContainment(StorageResolutionConfidence.Ambiguous).Status);
        Assert.Equal(PathIdentityStatus.Cancelled, PortablePathIdentityValidator.ValidateContainment(StorageResolutionConfidence.Resolved, new(true)).Status);
    }

    [Fact]
    public void HistoricalNoncanonicalKeysArePreservedAndPathReconciliationRefuses()
    {
        PathIdentityCandidate[] rows = [new(Guid.NewGuid(), Guid.NewGuid(), "clip.mp4", "clip.mp4")];
        Assert.True(PortablePathIdentityValidator.ValidateCandidates(rows).IsSafe);
        Assert.Equal(PathIdentityStatus.AmbiguousMapping, PortablePathIdentityValidator.ValidateReconciliation(rows, []).Status);
        Assert.Equal("clip.mp4", rows[0].LegacyKey);
    }
}
