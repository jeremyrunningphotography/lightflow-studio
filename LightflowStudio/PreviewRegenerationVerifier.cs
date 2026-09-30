using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

// Opt-in bounded package check. Every source and database is created beneath the explicit isolated profile.
internal static class PreviewRegenerationVerifier
{
    public static async Task<bool> RunAsync(LightflowStorageLocations profile, string[] args, InitializedDataProfile? initializedProfile = null)
    {
        if (!profile.IsIsolated) return false;
        var restart = args.Contains("--preview-regeneration-restart", StringComparer.Ordinal);
        var reportPath = Path.Combine(profile.ApplicationDataDirectory, restart ? "regeneration-restart.json" : "regeneration.json");
        var results = new List<object>();
        var timer = Stopwatch.StartNew();
        try
        {
            var initialized = initializedProfile ?? ApplicationDataProfile.Initialize(profile);
            initialized.RequireProfile(profile);
            var media = profile.ApplicationDataDirectory + "-sources";
            var ffmpeg = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe");
            if (!restart)
            {
                if (Directory.Exists(media)) throw new InvalidOperationException("Use a fresh isolated profile.");
                Directory.CreateDirectory(media);
                foreach (var (name, codec) in new[] { ("h264.mp4", "libopenh264"), ("hevc.mov", "libkvazaar"), ("mpeg4.mp4", "mpeg4") })
                {
                    var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                    foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
                        "testsrc2=size=160x96:rate=10:duration=3", "-c:v", codec, "-threads", "1", "-pix_fmt", "yuv420p" }) start.ArgumentList.Add(arg);
                    start.ArgumentList.Add(Path.Combine(media, name));
                    using var process = Process.Start(start)!;
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    Require(process.ExitCode == 0, error);
                }
                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 8, 96, 96, PixelFormats.Bgr24, null, new byte[16 * 8 * 3], 48)));
                using (var image = File.Create(Path.Combine(media, "image.jpg"))) encoder.Save(image);
                await File.WriteAllTextAsync(Path.Combine(media, "bad.mp4"), "deterministically invalid media");
            }
            await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: profile, initializedProfile: initialized)).Coordinator
                ?? throw new InvalidOperationException("Storage failed to start.");
            var root = (await storage.MediaRoots.ListAsync()).FirstOrDefault()
                ?? (await storage.MediaRoots.CreateAsync("Regeneration validation", media)).Root!;
            using var browser = new BrowserNavigationSession(storage.MediaRoots, storage.BrowserLocations,
                storage.MediaDiscovery, storage.MediaFolders, storage.BrowserRecursiveRoots, storage.RecursiveMediaDiscovery,
                storage.MediaAssets, storage.MediaTypes);
            var scheduler = (DerivedWorkScheduler)storage.DerivedWork!;
            async Task<BrowserFolderState> VisitAsync()
            {
                var state = await browser.NavigateToPathAsync(media) ?? throw new InvalidOperationException("Navigation failed.");
                if (state.DerivedWork is not { } work) throw new InvalidOperationException("No Browser work batch.");
                await work.Completion.WaitAsync(TimeSpan.FromSeconds(30));
                return state;
            }
            var state = await VisitAsync();
            var initial = scheduler.Diagnostics;
            Require(initial.ThumbnailAttempts == (restart ? 0 : 5), "Unexpected initial automatic work.");
            var assets = await storage.MediaAssets.ListAsync();
            Require(assets.Count == 5, "Unexpected source count.");
            var model = new BrowserGridModel();
            model.Populate(state.Entries);
            model.ApplyAssetIdentities(assets.Select(a => new CatalogReconciliationItem(a.AssetId, a.RelativePath,
                CatalogReconciliationItemStatus.Unchanged)).ToArray());
            var controls = new List<OrientedPreviewImage>();
            foreach (var asset in assets.Where(a => !a.RelativePath.EndsWith("bad.mp4")))
            {
                var record = (await storage.Previews!.GetAsync(asset.AssetId))!;
                Require(BrowserPreviewReuse.HasRetainedThumbnail(record, storage.Previews), "Expected usable Preview.");
                var path = MediaPathSemantics.ResolveContained(profile.PreviewsDirectory, record.ThumbnailRelativePath!);
                var tile = model.Tiles.Single(t => t.AssetId == asset.AssetId);
                var control = new OrientedPreviewImage();
                BindingOperations.SetBinding(control, System.Windows.Controls.Image.SourceProperty, new System.Windows.Data.Binding(nameof(BrowserGridTile.ThumbnailPath))
                { Source = tile, Converter = CachedPreviewImageConverter.Instance });
                controls.Add(control);
                model.ApplyThumbnail(asset.AssetId, path);
                model.ApplyPreviewFailure(asset.AssetId, record.ThumbnailState == PreviewComponentState.Failed ? record.ThumbnailFailureReason : null);
                Require(control.Source is BitmapSource, "Retained Preview did not decode.");
                if (restart && asset.RelativePath.EndsWith("image.jpg")) Require(tile.HasPreviewFailure, "Restart lost failed-replacement warning.");
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    var prior = control.Source;
                    model.ApplyThumbnailGenerating(asset.AssetId, true);
                    Require(!tile.HasPreviewFailure, "Active retry retained warning.");
                    var result = (await storage.RegenerateThumbnailsAsync([asset.AssetId])).Single();
                    results.Add(new { Source = asset.RelativePath, Repeat = repeat, Status = result.Status.ToString(), result.Diagnostic });
                    Require(result.Succeeded, result.Diagnostic ?? "Regeneration failed.");
                    model.ApplyThumbnail(asset.AssetId, result.ThumbnailPath!, reload: true);
                    model.ApplyThumbnailGenerating(asset.AssetId, false);
                    Require(control.Source is BitmapSource && !ReferenceEquals(prior, control.Source) && !tile.HasPreviewFailure,
                        "Same-path pixels did not refresh or warning remained.");
                    var after = (await storage.Previews.GetAsync(asset.AssetId))!;
                    Require(after.ThumbnailState == PreviewComponentState.Current && after.ThumbnailRetryAfterUtc is null, "Success did not clear failure.");
                }
            }
            var bad = assets.Single(a => a.RelativePath.EndsWith("bad.mp4"));
            var deadline = (await storage.Previews!.GetAsync(bad.AssetId))!.ThumbnailRetryAfterUtc;
            for (var visit = 0; visit < 10; visit++) await VisitAsync();
            Require(scheduler.Diagnostics.ThumbnailAttempts == initial.ThumbnailAttempts && scheduler.Diagnostics.MetadataAttempts == initial.MetadataAttempts,
                "Refresh bypassed automatic cooldown.");
            Require((await storage.Previews.GetAsync(bad.AssetId))!.ThumbnailRetryAfterUtc == deadline, "Observation moved deadline.");
            for (var retry = 0; retry < 2; retry++)
            {
                var failed = (await storage.RegenerateThumbnailsAsync([bad.AssetId])).Single();
                Require(!failed.Succeeded && failed.Status != ThumbnailGenerationStatus.Deferred, "Explicit failure retry was suppressed.");
                results.Add(new { Source = bad.RelativePath, Status = failed.Status.ToString(), Reason = failed.FailureReason.ToString(), failed.Diagnostic });
            }
            if (!restart)
            {
                var retained = assets.Single(a => a.RelativePath.EndsWith("image.jpg"));
                var before = (await storage.Previews.GetAsync(retained.AssetId))!;
                var path = MediaPathSemantics.ResolveContained(profile.PreviewsDirectory, before.ThumbnailRelativePath!);
                var bytes = await File.ReadAllBytesAsync(path);
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var failed = (await storage.RegenerateThumbnailsAsync([retained.AssetId])).Single();
                    Require(failed.Status == ThumbnailGenerationStatus.Failed, "External file lock did not fail replacement.");
                    results.Add(new { Source = retained.RelativePath, Stage = "promotion under external lock", Status = failed.Status.ToString(), failed.Diagnostic });
                }
                var after = (await storage.Previews.GetAsync(retained.AssetId))!;
                Require(bytes.SequenceEqual(await File.ReadAllBytesAsync(path)) && BrowserPreviewReuse.HasRetainedThumbnail(after, storage.Previews), "Failed replacement lost existing pixels.");
                await VisitAsync();
                Require((await storage.Previews.GetAsync(retained.AssetId))!.ThumbnailRetryAfterUtc == after.ThumbnailRetryAfterUtc, "Revisit retried failed replacement.");
                // Leave this failure for the separate clean-restart invocation to verify and recover.
            }
            Require(Directory.GetFiles(profile.PreviewsDirectory, "*.jpg", SearchOption.AllDirectories).Length == 4, "Unexpected cache growth.");
            Require(Directory.GetFiles(profile.PreviewsDirectory, "*.lightflow", SearchOption.AllDirectories).Length == 0, "Staging output leaked.");
            Require(scheduler.Diagnostics.Outstanding == 0, "Queue did not settle.");
            foreach (var database in new[] { profile.CatalogDatabasePath, profile.PreviewsDatabasePath })
            {
                using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";
                Require((string?)command.ExecuteScalar() == "ok", "Integrity check failed.");
                command.CommandText = "PRAGMA foreign_key_check;";
                using var reader = command.ExecuteReader(); Require(!reader.Read(), "Foreign-key check failed.");
            }
            GC.KeepAlive(controls);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { Phase = "passed", Restart = restart,
                ElapsedSeconds = timer.Elapsed.TotalSeconds, Initial = initial, Current = scheduler.Diagnostics,
                Artifacts = 4, Sources = 5, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { Phase = "failed", Error = error.ToString(), Results = results }));
            return false;
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
