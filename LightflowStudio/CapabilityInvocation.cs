using System.IO;

namespace LightflowStudio;

internal sealed record CapabilitySourceContext(Guid RootId, string RelativeFolder);

internal sealed record CapabilityInvocation(string Capability, IReadOnlyList<Guid> AssetIds,
    CapabilitySourceContext? SourceContext = null);

internal sealed record EncodingHandoffInput(Guid AssetId, Guid RootId, string SourcePath, string DisplayName,
    long FileSizeBytes, MediaRange? InitialTrim);

internal sealed record EncodingHandoffResult(IReadOnlyList<EncodingHandoffInput> Inputs,
    IReadOnlyList<string> Errors, string? InputFolder = null, bool IncludeSubfolders = false)
{
    public bool Succeeded => Errors.Count == 0 && Inputs.Count > 0;
}

/// <summary>Catalog-to-capability boundary. Asset identity stays durable until this materialization point.</summary>
internal sealed class EncodingCapabilityHandoff(IMediaAssetService assets, IMediaRootService roots,
    IMediaRangeStore ranges)
{
    public async Task<EncodingHandoffResult> MaterializeAsync(CapabilityInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(invocation.Capability, "video.encode", StringComparison.Ordinal))
            throw new ArgumentException("This handoff only accepts the video.encode capability.", nameof(invocation));
        if (invocation.AssetIds.Count == 0)
            return new([], ["Select at least one Catalog asset for Batch Encode."]);

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
            inputs.Add(new(assetId, resolved.Asset.RootId, resolved.PhysicalPath, name,
                resolved.Asset.FileSizeBytes, Snapshot(range)));
        }

        if (errors.Count != 0) return new([], errors);

        var inputFolder = await ResolveInputFolderAsync(invocation, inputs, cancellationToken).ConfigureAwait(false);
        if (inputFolder.Error is not null) return new([], [inputFolder.Error]);
        var includeSubfolders = inputs.Any(input => !string.Equals(Path.GetDirectoryName(input.SourcePath),
            inputFolder.Path, StringComparison.OrdinalIgnoreCase));
        return new(inputs, [], inputFolder.Path, includeSubfolders);
    }

    private async Task<(string? Path, string? Error)> ResolveInputFolderAsync(CapabilityInvocation invocation,
        IReadOnlyList<EncodingHandoffInput> inputs, CancellationToken cancellationToken)
    {
        if (invocation.SourceContext is not { } context)
            return (Path.GetDirectoryName(inputs[0].SourcePath), null);
        if (inputs.Any(input => input.RootId != context.RootId))
            return (null, "The selected assets no longer belong to the originating Media Root.");

        MediaPathResolution resolved;
        try { resolved = await roots.ResolveAsync(context.RootId, context.RelativeFolder, cancellationToken).ConfigureAwait(false); }
        catch (KeyNotFoundException) { return (null, "The originating Media Root no longer exists."); }
        if (resolved.RootAvailability != MediaRootAvailability.Online || !resolved.Exists || resolved.PhysicalPath is null)
            return (null, $"The originating Browser folder is offline or unavailable: {resolved.Diagnostic ?? "the folder could not be resolved"}");
        return (resolved.PhysicalPath, null);
    }

    private static MediaRange? Snapshot(MediaRange? range) => range is null
        ? null
        : new MediaRange(range.SourceDuration, range.In, range.Out);
}
