using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

// Opt-in packaged diagnostic; refuses normal user storage. Reports remain in the isolated profile.
internal static class PreviewQuiescenceVerifier
{
    internal static async Task<bool> RunAsync(LightflowStorageLocations profile, string[] args)
    {
        if (!profile.IsIsolated) return false;
        var report = Path.Combine(profile.ApplicationDataDirectory, "preview-quiescence.json");
        var restart = args.Contains("--preview-validation-restart", StringComparer.Ordinal);
        var durationIndex = Array.IndexOf(args, "--preview-validation-seconds");
        var seconds = durationIndex >= 0 && durationIndex + 1 < args.Length && int.TryParse(args[durationIndex + 1], out var value)
            ? Math.Clamp(value, 1, 3600) : 1020;
        var started = Stopwatch.StartNew();
        try
        {
            var parent = Path.GetDirectoryName(profile.ApplicationDataDirectory)!;
            var media = profile.ApplicationDataDirectory + "-sources";
            if (!restart)
            {
                if (Directory.Exists(media)) throw new InvalidOperationException("Use a fresh isolated validation profile.");
                Directory.CreateDirectory(media);
                await File.WriteAllTextAsync(Path.Combine(media, "bad.mp4"), "deterministically invalid video");
                var executable = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
                    "testsrc2=size=160x90:rate=10:duration=3", "-c:v", "mpeg4", "-color_range", "pc", Path.Combine(media, "valid.mp4") }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!;
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new InvalidOperationException(error);
            }
            var startup = Stopwatch.StartNew();
            await using var storage = (await LightflowStorageCoordinator.StartAsync(profile: profile)).Coordinator
                ?? throw new InvalidOperationException("Storage did not start.");
            var root = (await storage.MediaRoots.ListAsync()).FirstOrDefault(r => string.Equals(r.PhysicalPath, parent, StringComparison.OrdinalIgnoreCase))
                ?? (await storage.MediaRoots.CreateAsync("Isolated Preview validation", parent)).Root!;
            await storage.MediaMonitoring!.SynchronizeAsync();
            using var browser = new BrowserNavigationSession(storage.MediaRoots, storage.BrowserLocations,
                storage.MediaDiscovery, storage.MediaFolders, storage.BrowserRecursiveRoots, storage.RecursiveMediaDiscovery,
                storage.MediaAssets, storage.MediaTypes);
            var scheduler = (DerivedWorkScheduler)storage.DerivedWork!;
            async Task VisitAsync()
            {
                var state = await browser.NavigateToPathAsync(media);
                if (state?.DerivedWork is not { } batch) throw new InvalidOperationException("Browser did not schedule its normal batch.");
                await batch.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            }
            await VisitAsync();
            var startupMs = startup.ElapsedMilliseconds;
            var assets = (await storage.MediaAssets.ListAsync()).Where(a => a.RelativePath.EndsWith(".mp4")).ToArray();
            var bad = assets.Single(a => a.RelativePath.EndsWith("bad.mp4"));
            var valid = assets.Single(a => a.RelativePath.EndsWith("valid.mp4"));
            var failed = (await storage.Previews!.GetAsync(bad.AssetId))!;
            var good = (await storage.Previews.GetAsync(valid.AssetId))!;
            Require(failed.MetadataState == PreviewComponentState.Failed && failed.ThumbnailState == PreviewComponentState.Failed, "Failure not persisted.");
            Require(good.MetadataState == PreviewComponentState.Current && good.ThumbnailState == PreviewComponentState.Current, "Valid video did not complete.");
            var initial = scheduler.Diagnostics;
            Require(initial.MetadataAttempts == (restart ? 0 : 2) && initial.ThumbnailAttempts == (restart ? 0 : 2), "Unexpected initial attempts.");
            var artifactCount = Directory.GetFiles(profile.PreviewsDirectory, "*.jpg", SearchOption.AllDirectories).Length;
            var assetCount = (await storage.MediaAssets.ListAsync()).Count;
            var observation = Stopwatch.StartNew();
            var visits = 0;
            async Task ReportAsync(string phase) => await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
            {
                Phase = phase, Restart = restart, ElapsedSeconds = started.Elapsed.TotalSeconds,
                ObservationSeconds = observation.Elapsed.TotalSeconds, StartupAndFirstVisitMs = startupMs,
                Visits = visits, Initial = initial, Current = scheduler.Diagnostics, AssetCount = assetCount,
                Artifacts = Directory.GetFiles(profile.PreviewsDirectory, "*.jpg", SearchOption.AllDirectories).Length,
                MetadataDeadline = failed.MetadataRetryAfterUtc, ThumbnailDeadline = failed.ThumbnailRetryAfterUtc
            }, new JsonSerializerOptions { WriteIndented = true }));
            while (observation.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                await VisitAsync(); visits++;
                // Actual FileSystemWatcher receives both ordinary external hints and owned output writes.
                File.SetLastWriteTimeUtc(Path.Combine(media, "bad.mp4"), new DateTime(bad.LastWriteUtcTicks, DateTimeKind.Utc));
                await File.WriteAllTextAsync(Path.Combine(profile.PreviewsDirectory, "validation-owned.lightflow"), visits.ToString());
                await Task.Delay(750);
                await storage.MediaMonitoring.FlushAsync();
                await VisitAsync();
                var now = scheduler.Diagnostics;
                Require(now.MetadataAttempts == initial.MetadataAttempts && now.ThumbnailAttempts == initial.ThumbnailAttempts, "Unchanged work executed again.");
                Require(Directory.GetFiles(profile.PreviewsDirectory, "*.jpg", SearchOption.AllDirectories).Length == artifactCount, "Artifact count grew.");
                Require((await storage.MediaAssets.ListAsync()).Count == assetCount, "Owned files became Catalog candidates.");
                Require((await storage.Previews.GetAsync(bad.AssetId))!.MetadataRetryAfterUtc == failed.MetadataRetryAfterUtc, "Reads extended cooldown.");
                await ReportAsync("observing");
                await Task.Delay(4250);
            }
            if (!restart)
            {
                await File.AppendAllTextAsync(Path.Combine(media, "bad.mp4"), "material source change");
                await VisitAsync();
                var changed = scheduler.Diagnostics;
                Require(changed.MetadataAttempts == initial.MetadataAttempts + 1 && changed.ThumbnailAttempts == initial.ThumbnailAttempts + 1,
                    "Source change did not permit one retry.");
                // A valid external creation is still discovered by the real watcher without Browser demand.
                File.Copy(Path.Combine(media, "valid.mp4"), Path.Combine(media, "external.mp4"));
                var limit = Stopwatch.StartNew();
                while (limit.Elapsed < TimeSpan.FromSeconds(20))
                {
                    await Task.Delay(250);
                    var external = (await storage.MediaAssets.ListAsync()).FirstOrDefault(a => a.RelativePath.EndsWith("external.mp4"));
                    if (external is not null && (await storage.Previews.GetAsync(external.AssetId))?.ThumbnailState == PreviewComponentState.Current) break;
                }
                var externalAsset = (await storage.MediaAssets.ListAsync()).SingleOrDefault(a => a.RelativePath.EndsWith("external.mp4"));
                Require(externalAsset is not null && (await storage.Previews.GetAsync(externalAsset.AssetId))?.ThumbnailState == PreviewComponentState.Current,
                    "External watcher change did not complete a Preview.");
            }
            await Task.Delay(1000);
            Require(scheduler.Diagnostics.Outstanding == 0, "Queue failed to settle.");
            foreach (var path in new[] { profile.CatalogDatabasePath, profile.PreviewsDatabasePath })
            {
                using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
                connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check;";
                Require((string?)command.ExecuteScalar() == "ok", "Database integrity check failed.");
            }
            await ReportAsync("passed");
            return true;
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Phase = "failed", Error = exception.ToString(), ElapsedSeconds = started.Elapsed.TotalSeconds }));
            return false;
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
