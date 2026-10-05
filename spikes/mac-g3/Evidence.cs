using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lightflow.G3;

internal static class Evidence
{
    internal static void Run()
    {
        var results = new List<object>();
        foreach (var count in new[] { 10_000, 100_000 })
        {
            var allocation = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            var window = new ProofWindow(count);
            window.Show(); Flush(window);
            var populationMs = timer.Elapsed.TotalMilliseconds;
            foreach (var details in new[] { false, true })
            {
                window.ShowDetails(details); Flush(window);
                var list = details ? window.Details : window.Tiles;
                var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
                var samples = new List<double>();
                var maxRealized = 0;
                var totalObservations = 0;
                for (var step = 0; step < 80; step++)
                {
                    timer.Restart();
                    var index = (int)((long)step * (list.ItemCount - 1) / 79);
                    list.ScrollIntoView(index); Flush(window);
                    samples.Add(timer.Elapsed.TotalMilliseconds);
                    var realized = list.GetRealizedContainers().ToArray();
                    maxRealized = Math.Max(maxRealized, realized.Length);
                    totalObservations += realized.Length;
                    foreach (var container in realized) seen.Add(container);
                }
                window.SelectTile(31, false, false); window.SelectTile(37, true, false);
                window.ShowDetails(true); Flush(window);
                var selected = window.Details.SelectedItems!.Cast<MediaRow>().Select(x => x.Id).Order().ToArray();
                if (!selected.SequenceEqual(Enumerable.Range(31, 7))) throw new InvalidOperationException("Grid→Details selection mismatch");
                window.ShowDetails(false); Flush(window);
                if (!window.SelectedIds.SequenceEqual(selected)) throw new InvalidOperationException("Details→Grid selection mismatch");
                window.Search.Focus(); window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
                if (!window.SelectedIds.SequenceEqual(selected)) throw new InvalidOperationException("Text editor stole Browser navigation");
                samples.Sort();
                results.Add(new { mode = details ? "Details18Columns" : "Grid", count, populationMs,
                    scrollLayoutP50Ms = samples[40], scrollLayoutP95Ms = samples[75], maxRealized,
                    distinctContainers = seen.Count, totalContainerObservations = totalObservations,
                    reuseObserved = seen.Count < totalObservations, tileTemplateBuilds = window.TileBuilds,
                    thumbnailLoads = window.ThumbnailLoads, thumbnailDisposals = window.ThumbnailDisposals,
                    managedAllocatedBytesSinceWindowStart = GC.GetTotalAllocatedBytes(true) - allocation,
                    managedLiveBytes = GC.GetTotalMemory(true), processPrivateBytes = Process.GetCurrentProcess().PrivateMemorySize64,
                    selectionTransfer = true, localTextInputOwnership = true,
                    measurement = "Headless software render/layout; not native frame latency" });
                window.ShowDetails(details); Flush(window);
                using var capture = window.CaptureRenderedFrame();
                capture?.Save(Path.Combine(Program.DataRoot, $"{count}-{(details ? "details" : "grid")}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            window.Close(); Dispatcher.UIThread.RunJobs();
            results.Add(new { test = "bitmap-teardown", count, window.ThumbnailLoads, window.ThumbnailDisposals,
                balanced = window.ThumbnailLoads == window.ThumbnailDisposals });
            if (window.ThumbnailLoads != window.ThumbnailDisposals) throw new InvalidOperationException("Unbalanced thumbnail lifetime");
        }
        var settings = new ShortcutWindow(); settings.Show(); Flush(settings);
        using (var capture = settings.CaptureRenderedFrame()) capture?.Save(Path.Combine(Program.DataRoot, "settings.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        settings.Close(); Dispatcher.UIThread.RunJobs();
        var report = new { timestampUtc = DateTime.UtcNow, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
            avalonia = typeof(Window).Assembly.GetName().Version?.ToString(), backend = "Avalonia.Headless + Skia software",
            measurements = results, images = ImageEvidence.Run(Path.Combine(Program.DataRoot, "fixtures")),
            disposition = "G3 pending actual Apple Silicon and agreed thresholds; Windows headless evidence only" };
        File.WriteAllText(Path.Combine(Program.DataRoot, "UI_PERFORMANCE_RESULTS.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Evidence written to " + Program.DataRoot);
    }
    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
}
