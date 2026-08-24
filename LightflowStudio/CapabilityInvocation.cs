using System.IO;

namespace LightflowStudio;

internal sealed record CapabilitySourceContext(Guid RootId, string RelativeFolder);

internal sealed record CapabilityInvocation(string Capability, IReadOnlyList<Guid> AssetIds,
    CapabilitySourceContext? SourceContext = null);

internal sealed record EncodingHandoffInput(Guid AssetId, Guid RootId, string SourcePath, string DisplayName,
    long FileSizeBytes, MediaRange? InitialTrim, MaterializedColorPipeline? AssignedColor = null);

internal sealed record EncodingHandoffResult(IReadOnlyList<EncodingHandoffInput> Inputs,
    IReadOnlyList<string> Errors, string? InputFolder = null, bool IncludeSubfolders = false)
{
    public bool Succeeded => Errors.Count == 0 && Inputs.Count > 0;
}

/// <summary>Catalog-to-capability boundary. Asset identity stays durable until this materialization point.</summary>
internal sealed class EncodingCapabilityHandoff
{
    private readonly IMediaAssetService _assets;
    private readonly IMediaRootService _roots;
    private readonly IMediaRangeStore _ranges;
    private readonly IAssetColorStore? _colors;
    private readonly ILutLibraryCache? _lutCache;
    private readonly IEncodingLutResourceStore? _resourceStore;

    public EncodingCapabilityHandoff(IMediaAssetService assets, IMediaRootService roots, IMediaRangeStore ranges)
        : this(assets, roots, ranges, null, null, null) { }

    public EncodingCapabilityHandoff(IMediaAssetService assets, IMediaRootService roots, IMediaRangeStore ranges,
        IAssetColorStore? colors, ILutLibraryCache? lutCache, IEncodingLutResourceStore? resourceStore)
    {
        _assets = assets;
        _roots = roots;
        _ranges = ranges;
        _colors = colors;
        _lutCache = lutCache;
        _resourceStore = resourceStore;
    }

    public async Task<EncodingHandoffResult> MaterializeAsync(CapabilityInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(invocation.Capability, "video.encode", StringComparison.Ordinal))
            throw new ArgumentException("This handoff only accepts the video.encode capability.", nameof(invocation));
        if (invocation.AssetIds.Count == 0)
            return new([], ["Select at least one Catalog asset to Export."]);

        var inputs = new List<EncodingHandoffInput>(invocation.AssetIds.Count);
        var errors = new List<string>();
        foreach (var assetId in invocation.AssetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await _assets.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                errors.Add($"Asset {assetId:D} is no longer in the Catalog.");
                continue;
            }

            var name = Path.GetFileName(resolved.Asset.RelativePath);
            if (!string.Equals(resolved.Asset.MediaType, "video", StringComparison.OrdinalIgnoreCase) ||
                !MediaFileCatalog.IsSupported(resolved.Asset.RelativePath))
            {
                errors.Add($"{name} is not supported by Export.");
                continue;
            }
            if (resolved.RootAvailability != MediaRootAvailability.Online ||
                !resolved.SourceExists || string.IsNullOrWhiteSpace(resolved.PhysicalPath))
            {
                errors.Add($"{name} is offline or unavailable: {resolved.Diagnostic ?? "the source could not be resolved"}");
                continue;
            }

            var range = await _ranges.RestoreAsync(assetId, cancellationToken).ConfigureAwait(false);
            var color = await MaterializeColorAsync(assetId, cancellationToken).ConfigureAwait(false);
            inputs.Add(new(assetId, resolved.Asset.RootId, resolved.PhysicalPath, name,
                resolved.Asset.FileSizeBytes, Snapshot(range), color));
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

        MediaRootInfo? root;
        try { root = await _roots.GetAsync(context.RootId, cancellationToken).ConfigureAwait(false); }
        catch (KeyNotFoundException) { return (null, "The originating Media Root no longer exists."); }
        if (root is null) return (null, "The originating Media Root no longer exists.");
        if (root.Availability != MediaRootAvailability.Online || string.IsNullOrWhiteSpace(root.PhysicalPath))
            return (null, $"The originating Browser folder is offline or unavailable: {root.Diagnostic ?? "the Media Root is not connected"}");
        string folder;
        try
        {
            folder = string.IsNullOrWhiteSpace(context.RelativeFolder)
                ? root.PhysicalPath
                : MediaPathSemantics.ResolveContained(root.PhysicalPath, context.RelativeFolder);
        }
        catch (ArgumentException exception) { return (null, $"The originating Browser folder is invalid: {exception.Message}"); }
        if (!Directory.Exists(folder))
            return (null, "The originating Browser folder is missing beneath an available Media Root.");
        return (folder, null);
    }

    private static MediaRange? Snapshot(MediaRange? range) => range is null
        ? null
        : new MediaRange(range.SourceDuration, range.In, range.Out);

    private async Task<MaterializedColorPipeline?> MaterializeColorAsync(Guid assetId,
        CancellationToken cancellationToken)
    {
        if (_colors is null || _lutCache is null || _resourceStore is null) return null;
        var intent = await _colors.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        return new(intent.IsActive,
            await MaterializeStageAsync(ColorLutStage.Camera, intent.Camera, cancellationToken).ConfigureAwait(false),
            await MaterializeStageAsync(ColorLutStage.Creative, intent.Creative, cancellationToken).ConfigureAwait(false));
    }

    private async Task<MaterializedLutResource?> MaterializeStageAsync(ColorLutStage stage,
        ColorLutReference? reference, CancellationToken cancellationToken)
    {
        if (reference is null) return null;
        var current = _lutCache!.Snapshot(stage).Resources.FirstOrDefault(resource =>
            string.Equals(resource.ContentSha256, reference.ContentSha256, StringComparison.Ordinal));
        if (current is not null)
            return await _resourceStore!.SnapshotAsync(stage, current, cancellationToken).ConfigureAwait(false);
        var hash = reference.ContentSha256.ToLowerInvariant();
        var key = hash.Length >= 2 ? $"{hash[..2]}/{hash}.cube" : $"invalid/{reference.LutId:D}.cube";
        return new(reference.LutId, stage, reference.DisplayName, hash, key);
    }
}
