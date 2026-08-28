using System.IO;

namespace LightflowStudio;

internal sealed record CapabilitySourceContext(Guid RootId, string RelativeFolder);

internal sealed record CapabilityInvocation(string Capability, IReadOnlyList<Guid> AssetIds,
    CapabilitySourceContext? SourceContext = null);

internal sealed record EncodingHandoffInput(Guid AssetId, Guid RootId, string SourcePath, string DisplayName,
    long FileSizeBytes, MediaRange? InitialTrim, MaterializedColorPipeline? AssignedColor = null,
    ExportItemProvenance? ExportProvenance = null,
    string? NamingOriginalName = null,
    string? NamingIndexNumberBasis = null,
    bool RangeIsFixed = false);

internal sealed record EncodingHandoffResult(IReadOnlyList<EncodingHandoffInput> Inputs,
    IReadOnlyList<string> Errors, string? InputFolder = null, bool IncludeSubfolders = false)
{
    public bool Succeeded => Errors.Count == 0 && Inputs.Count > 0;
}

internal enum SubclipExportEntryKind { BrowserSources, PlayerSelection }

internal sealed record SubclipExportInvocation(
    SubclipExportEntryKind EntryKind,
    IReadOnlyList<Guid> AssetIds,
    IReadOnlyList<Guid>? SelectedSubclipIds = null,
    CapabilitySourceContext? SourceContext = null,
    bool IncludeNoSubclipSources = false);

/// <summary>Builds typed prospective Subclip/fallback items while reusing the ordinary Catalog source/Color handoff.</summary>
internal sealed class SubclipExportCapabilityHandoff(
    EncodingCapabilityHandoff sources,
    ISubclipService subclips)
{
    public async Task<EncodingHandoffResult> MaterializeAsync(SubclipExportInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (invocation.AssetIds.Count == 0)
            return new([], ["Select at least one Catalog video to Export Subclips."]);

        var ordinary = await sources.MaterializeAsync(new CapabilityInvocation("video.encode", invocation.AssetIds,
            invocation.SourceContext), cancellationToken).ConfigureAwait(false);
        if (!ordinary.Succeeded) return ordinary;

        var output = new List<EncodingHandoffInput>();
        if (invocation.EntryKind == SubclipExportEntryKind.PlayerSelection)
        {
            if (ordinary.Inputs.Count != 1)
                return new([], ["Player Export selected requires exactly one current source."]);
            var selected = invocation.SelectedSubclipIds?.ToHashSet() ?? [];
            if (selected.Count == 0) return new([], ["Select at least one Subclip to export."]);
            var source = ordinary.Inputs[0];
            var current = await subclips.ListAsync(source.AssetId, cancellationToken).ConfigureAwait(false);
            var missing = selected.Where(id => current.All(item => item.SubclipId != id)).ToArray();
            if (missing.Length != 0)
                return new([], ["One or more selected Subclips no longer exist for the current source. Review the selection and try again."]);
            foreach (var subclip in current.Where(item => selected.Contains(item.SubclipId)))
                output.Add(ForSubclip(source, subclip));
        }
        else
        {
            foreach (var source in ordinary.Inputs)
            {
                var current = await subclips.ListAsync(source.AssetId, cancellationToken).ConfigureAwait(false);
                if (current.Count != 0)
                {
                    foreach (var subclip in current) output.Add(ForSubclip(source, subclip));
                }
                else if (invocation.IncludeNoSubclipSources)
                {
                    output.Add(source with
                    {
                        InitialTrim = null,
                        RangeIsFixed = true,
                        ExportProvenance = new(ExportItemKind.NoSubclipFullSourceFallback, source.AssetId),
                        NamingOriginalName = Path.GetFileNameWithoutExtension(source.SourcePath),
                        NamingIndexNumberBasis = Path.GetFileNameWithoutExtension(source.SourcePath)
                    });
                }
            }
        }

        if (output.Count == 0)
            return new([], ["The selected videos do not currently contain any Subclips. Enable the full-source fallback to include videos with no Subclips."], ordinary.InputFolder, ordinary.IncludeSubfolders);
        return new(output, [], ordinary.InputFolder, ordinary.IncludeSubfolders);
    }

    private static EncodingHandoffInput ForSubclip(EncodingHandoffInput source, Subclip subclip) => source with
    {
        InitialTrim = new(subclip.SourceDuration, subclip.In, subclip.Out),
        RangeIsFixed = true,
        ExportProvenance = new(ExportItemKind.Subclip, source.AssetId, subclip.SubclipId, subclip.Name, subclip.Revision),
        NamingOriginalName = subclip.Name,
        NamingIndexNumberBasis = Path.GetFileNameWithoutExtension(source.SourcePath)
    };
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
                resolved.Asset.FileSizeBytes, Snapshot(range), color,
                new(ExportItemKind.OrdinarySource, assetId)));
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
