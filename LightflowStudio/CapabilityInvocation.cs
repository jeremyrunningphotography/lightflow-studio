using System.IO;

namespace LightflowStudio;

internal sealed record CapabilityInvocation(string Capability, IReadOnlyList<Guid> AssetIds);

internal sealed record EncodingHandoffInput(Guid AssetId, string SourcePath, string DisplayName,
    long FileSizeBytes, MediaRange? InitialTrim);

internal sealed record EncodingHandoffResult(IReadOnlyList<EncodingHandoffInput> Inputs,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0 && Inputs.Count > 0;
}

/// <summary>Catalog-to-capability boundary. Asset identity stays durable until this materialization point.</summary>
internal sealed class EncodingCapabilityHandoff(IMediaAssetService assets, IMediaRangeStore ranges)
{
    public async Task<EncodingHandoffResult> MaterializeAsync(CapabilityInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(invocation.Capability, "video.encode", StringComparison.Ordinal))
            throw new ArgumentException("This handoff only accepts the video.encode capability.", nameof(invocation));

        var inputs = new List<EncodingHandoffInput>(invocation.AssetIds.Count);
        var errors = new List<string>();
        foreach (var assetId in invocation.AssetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await assets.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                errors.Add($"Asset {assetId:D} is no longer in the Catalog.");
                continue;
            }

            var name = Path.GetFileName(resolved.Asset.RelativePath);
            if (!string.Equals(resolved.Asset.MediaType, "video", StringComparison.OrdinalIgnoreCase) ||
                !MediaFileCatalog.IsSupported(resolved.Asset.RelativePath))
            {
                errors.Add($"{name} is not supported by Batch Encode.");
                continue;
            }
            if (resolved.RootAvailability != MediaRootAvailability.Online ||
                !resolved.SourceExists || string.IsNullOrWhiteSpace(resolved.PhysicalPath))
            {
                errors.Add($"{name} is offline or unavailable: {resolved.Diagnostic ?? "the source could not be resolved"}");
                continue;
            }

            var range = await ranges.RestoreAsync(assetId, cancellationToken).ConfigureAwait(false);
            inputs.Add(new(assetId, resolved.PhysicalPath, name, resolved.Asset.FileSizeBytes, Snapshot(range)));
        }

        return errors.Count == 0
            ? new(inputs, [])
            : new([], errors);
    }

    private static MediaRange? Snapshot(MediaRange? range) => range is null
        ? null
        : new MediaRange(range.SourceDuration, range.In, range.Out);
}
