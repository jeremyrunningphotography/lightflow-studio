using Lightflow.Application;
using Lightflow.Domain;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

public sealed class NativePathIdentityTests
{
    [Fact]
    public async Task NativeNamesAndSymlinkFactsFeedSharedValidatorWithoutMutation()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var workspace = Path.Combine(AppContext.BaseDirectory, "fixtures", "lf009-" + Guid.NewGuid().ToString("N"));
        var root = Directory.CreateDirectory(Path.Combine(workspace, "media")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(workspace, "outside")).FullName;
        try
        {
            var id = Guid.NewGuid();
            File.WriteAllText(Path.Combine(root, "Photo.mp4"), "first");
            File.WriteAllText(Path.Combine(root, "photo.mp4"), "second");
            File.WriteAllText(Path.Combine(root, "café.mp4"), "unicode-one");
            File.WriteAllText(Path.Combine(root, "cafe\u0301.mp4"), "unicode-two");
            var files = Directory.EnumerateFiles(root).ToArray();
            var before = files.ToDictionary(p => p, File.ReadAllBytes);
            var observed = files.Select(p => new PathIdentityCandidate(id, Guid.Empty, Path.GetFileName(p))).ToArray();
            var result = PortablePathIdentityValidator.ValidateCandidates(observed);
            if (files.Length > 2) Assert.Equal(PathIdentityStatus.AmbiguousMapping, result.Status);
            else Assert.True(result.IsSafe); // Native insensitive volumes may expose one object per equivalent name.
            foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));

            var alias = Path.Combine(root, "linked"); Directory.CreateSymbolicLink(alias, outside);
            Assert.True((File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0);
            Assert.Equal(PathIdentityStatus.UnsafeContainment,
                PortablePathIdentityValidator.ValidateContainment(StorageResolutionConfidence.Ambiguous).Status);
            var request = new StorageAssessmentRequest(Guid.NewGuid(), 0, StorageRole.MediaSource, StorageOperation.Read, root);
            using (var unconfigured = new MacStorageLocationAssessor([]))
                Assert.False(PortablePathIdentityValidator.ValidateNativeMapping(request,
                    await unconfigured.AssessAsync(request), DateTimeOffset.UtcNow).IsSafe);
            using var assessor = new MacStorageLocationAssessor([outside]);
            var first = await assessor.AssessAsync(request);
            var second = await assessor.AssessAsync(request);
            Assert.True(PortablePathIdentityValidator.ValidateNativeMapping(request, first, DateTimeOffset.UtcNow).IsSafe, first.ProviderDiagnostic);
            Assert.True(PortablePathIdentityValidator.ValidateNativeMapping(request, second, DateTimeOffset.UtcNow, first).IsSafe, second.ProviderDiagnostic);
            Assert.NotNull(first.Identity);
            Directory.Delete(alias);
        }
        finally { Directory.Delete(workspace, true); }
    }
}
