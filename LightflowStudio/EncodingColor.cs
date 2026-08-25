using System.IO;
using System.Security.Cryptography;

namespace LightflowStudio;

internal enum EncodingColorMode { OriginalOrManual, Assigned }

internal sealed record MaterializedLutResource(
    Guid LutId,
    ColorLutStage Stage,
    string DisplayName,
    string ContentSha256,
    string ResourceKey);

internal sealed record MaterializedColorPipeline
{
    // colorEnabled remains a constructor parameter for serialized pre-#167 compatibility. Current LUT Color
    // activity is derived exclusively from the final materialized stages.
    public MaterializedColorPipeline(bool colorEnabled, MaterializedLutResource? Camera = null,
        MaterializedLutResource? Creative = null)
    { this.Camera = Camera; this.Creative = Creative; }

    public bool ColorEnabled => HasAssignments;
    public MaterializedLutResource? Camera { get; init; }
    public MaterializedLutResource? Creative { get; init; }
    public IReadOnlyList<MaterializedLutResource> OrderedPipeline =>
        new[] { Camera, Creative }.OfType<MaterializedLutResource>().ToArray();
    public bool HasAssignments => Camera is not null || Creative is not null;
    public bool ShouldRender(EncodingColorMode mode) => mode == EncodingColorMode.Assigned && ColorEnabled;
}

internal interface IEncodingLutResourceStore
{
    Task<MaterializedLutResource> SnapshotAsync(ColorLutStage stage, ManagedLutResource resource,
        CancellationToken cancellationToken = default);
    string Resolve(MaterializedLutResource resource);
}

/// <summary>Content-addressed, job-owned copies used by running and historical Encoding jobs.</summary>
internal sealed class EncodingLutResourceStore(string rootDirectory) : IEncodingLutResourceStore
{
    public static string DefaultDirectory => Path.Combine(
        LightflowStorageLocations.Current.ApplicationDataDirectory, "encoding-resources", "luts");

    public async Task<MaterializedLutResource> SnapshotAsync(ColorLutStage stage, ManagedLutResource resource,
        CancellationToken cancellationToken = default)
    {
        if (resource.Availability != LutResourceAvailability.Available || string.IsNullOrWhiteSpace(resource.FilePath))
            throw new FileNotFoundException(resource.Diagnostic ?? $"The {StageName(stage)} LUT is unavailable.");
        var expected = NormalizeHash(resource.ContentSha256);
        var bytes = await File.ReadAllBytesAsync(resource.FilePath, cancellationToken).ConfigureAwait(false);
        var validation = CubeLutValidator.Validate(bytes);
        if (!validation.IsValid)
            throw new InvalidDataException($"The {StageName(stage)} LUT '{resource.DisplayName}' is invalid: {validation.Diagnostic}");
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"The {StageName(stage)} LUT '{resource.DisplayName}' no longer matches its Catalog identity.");

        var key = Path.Combine(expected[..2], expected + ".cube").Replace('\\', '/');
        var destination = PathFor(key);
        if (!File.Exists(destination))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                try { File.Move(temporary, destination, false); }
                catch (IOException) when (File.Exists(destination)) { }
            }
            finally { try { File.Delete(temporary); } catch (IOException) { } }
        }
        return new(resource.LutId, stage, resource.DisplayName, expected, key);
    }

    public string Resolve(MaterializedLutResource resource)
    {
        var expected = NormalizeHash(resource.ContentSha256);
        var path = PathFor(resource.ResourceKey);
        if (!File.Exists(path))
            throw new FileNotFoundException($"The materialized {StageName(resource.Stage)} LUT '{resource.DisplayName}' is missing.", path);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new IOException($"The materialized {StageName(resource.Stage)} LUT '{resource.DisplayName}' could not be read.", exception); }
        var validation = CubeLutValidator.Validate(bytes);
        if (!validation.IsValid)
            throw new InvalidDataException($"The materialized {StageName(resource.Stage)} LUT '{resource.DisplayName}' is invalid: {validation.Diagnostic}");
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"The materialized {StageName(resource.Stage)} LUT '{resource.DisplayName}' does not match its saved content identity.");
        return path;
    }

    private string PathFor(string key)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The materialized LUT resource key is invalid.");
        return candidate;
    }

    private static string NormalizeHash(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The materialized LUT content identity is invalid.");
        return normalized;
    }

    internal static string StageName(ColorLutStage stage) => stage == ColorLutStage.Camera ? "Camera" : "Creative";
}
