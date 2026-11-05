using System.Security.Cryptography;
using System.Text.Json;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

/// <summary>Read-only authored intent; only explicitly configured LUT roots are inspected, never Catalog writes.</summary>
internal sealed class MacPreviewColorSource : IThumbnailColorSource
{
    private readonly CatalogAssetColorStore _store;
    private readonly NativeLuts _luts;
    private readonly string _cache;
    private readonly MacCatalogNativeEffects _guard;
    public MacPreviewColorSource(CatalogLifecycle owner, MacCatalogNativeEffects guard)
    {
        _guard = guard; _cache = Path.Combine(owner.Locations.PreviewsDirectory, "luts");
        using var configuration = JsonDocument.Parse(owner.Settings.Serialize());
        string Folder(string key) => configuration.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        _luts = new(Folder("CameraLutFolder"), Folder("CreativeLutFolder"));
        _store = new(() => owner.Session, _luts);
    }
    public async Task<string> IdentityAsync(Guid id, CancellationToken token)
    {
        var intent = await _store.GetAsync(id, token).ConfigureAwait(false);
        return intent.HasColor ? intent.ColorIdentity : PreviewVisualIdentity.Original;
    }
    public async Task<ThumbnailColorRender> ResolveAsync(Guid id, CancellationToken token)
    {
        var intent = await _store.GetAsync(id, token).ConfigureAwait(false);
        if (!intent.HasColor) return ThumbnailColorRender.Original;
        var paths = new List<string>();
        foreach (var (stage, reference) in new[] { (ColorLutStage.Camera, intent.Camera), (ColorLutStage.Creative, intent.Creative) })
        {
            if (reference is null) continue; token.ThrowIfCancellationRequested();
            var resource = _luts.Scan(stage, token).Resources.FirstOrDefault(x => x.ContentSha256 == reference.ContentSha256);
            if (resource?.FilePath is not { } source) throw new FileNotFoundException("An authored LUT is unavailable in its configured local folder; untreated pixels were not substituted.");
            var old = NativeLuts.Begin();
            byte[] bytes;
            try { bytes = File.ReadAllBytes(source); } finally { NativeLuts.End(old); }
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), reference.ContentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An authored LUT changed while being read.");
            _guard.GuardAccess(_cache); Directory.CreateDirectory(_cache);
            var path = Path.Combine(_cache, reference.ContentSha256.ToLowerInvariant() + ".cube"); _guard.GuardAccess(path);
            if (!File.Exists(path) || !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), reference.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".lightflow";
                try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
                finally { File.Delete(temporary); }
            }
            paths.Add(path);
        }
        return new(intent.ColorIdentity, paths);
    }
    private sealed class NativeLuts(string camera, string creative) : ILutLibraryCache
    {
        [System.Runtime.InteropServices.DllImport("libLightflowStorage.dylib")] private static extern int lf_preview_begin();
        [System.Runtime.InteropServices.DllImport("libLightflowStorage.dylib")] private static extern int lf_preview_end(int old);
        public static int Begin() { var old = lf_preview_begin(); if (old < 0) throw new IOException("Cloud materialization could not be disabled for LUT reads."); return old; }
        public static void End(int old) { if (lf_preview_end(old) != 0) throw new IOException("Source read policy could not be restored."); }
        // Catalog intent reads must never scan a media/LUT folder. Availability is resolved only by workers.
        public LutLibrarySnapshot Snapshot(ColorLutStage stage) => new(stage == ColorLutStage.Camera ? camera : creative, [], []);
        public LutLibrarySnapshot Scan(ColorLutStage stage, CancellationToken token)
        {
            var folder = stage == ColorLutStage.Camera ? camera : creative;
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return new(folder, [], []);
            // Native identity/link and resident checks protect explicitly configured roots. No recursive scan.
            new MacCatalogNativeEffects(Path.GetPathRoot(folder)!).RequireUnlinked(folder);
            var old = Begin();
            try
            {
                var scan = FolderLutScanner.Scan(folder, token);
                return new(folder, scan.Candidates.Select(x => new ManagedLutResource(Guid.Empty, x.DisplayName, x.FileName, x.ContentSha256, LutDimension.ThreeDimensional, x.Size, LutResourceAvailability.Available, x.FilePath)).ToArray(), scan.Problems);
            }
            finally { End(old); }
        }
        public Task InitializeAsync(string a, string b, CancellationToken token = default) => Task.CompletedTask;
        public Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder, CancellationToken token = default) => Task.FromResult(Snapshot(stage));
        public ManagedLutResource? Get(ColorLutStage stage, Guid id) => null;
        public string ResolvePath(ColorLutStage stage, Guid id) => throw new FileNotFoundException("LUT resolution requires authored content identity.");
        public Task<CubeLutData> GetRuntimeAsync(ColorLutStage stage, Guid id, CancellationToken token = default) => throw new NotSupportedException();
    }
}
