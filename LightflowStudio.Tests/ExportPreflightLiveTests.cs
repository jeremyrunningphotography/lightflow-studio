using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class ExportPreflightLiveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JobsIndicatorsFooterAndButtonTransitionTogetherWithoutEofWarnings(bool failSecond)
    {
        var root = Directory.CreateTempSubdirectory("export-progress-live-").FullName;
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();
                var inputs = Enumerable.Range(0, 2).Select(index =>
                {
                    var path = Path.Combine(root, $"clip-{index}.mp4");
                    File.WriteAllText(path, "fixture");
                    var asset = Guid.NewGuid();
                    return new EncodingHandoffInput(asset, Guid.NewGuid(), path, $"clip-{index}", 7,
                        new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.04), TimeSpan.FromSeconds(.2)), ExportProvenance: new(ExportItemKind.Subclip, asset, Guid.NewGuid(), $"DJI timing fixture {index + 1}", 1),
                        NamingOriginalName: $"DJI timing fixture {index + 1}", RangeIsFixed: true, SourceIdentity: TrimSourceIdentity.Read(path));
                }).ToArray();
                var model = new ExportDialogModel(new(inputs, [], root), new(), [], [], new EncodingLutResourceStore(Path.Combine(root, "luts")), _ => new(false, 0), namingDefault: ExportNamingDefault.Subclip);
                var runner = new ControlledRunner(failSecond);
                var coordinator = new ExportPreflightCoordinator(new("ffprobe", runner), _ => Task.FromResult(
                    new EncoderCapability(EncoderBackend.NvidiaNvenc, EncoderCapabilityState.ImplementedAndAvailable, "Execution probe succeeded")));
                var window = new ExportDialog(model, null!, null, coordinator)
                { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
                var ticks = 0;
                var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                heartbeat.Tick += (_, _) => ticks++;
                try
                {
                    window.Show(); heartbeat.Start();
                    await Until(() => runner.Entered == 2);
                    // Check dispatcher liveness while both probe gates remain blocked.
                    // A fixed 100 ms sample depends on runner timer/render scheduling.
                    await Until(() => ticks >= 2, 2000,
                        "Dispatcher heartbeat did not tick twice while preflight was pending.");
                    Assert.True(ticks >= 2);
                    window.UpdateLayout();
                    var rings = Descendants<JobsRadialProgress>(window).ToArray();
                    Assert.Equal(2, rings.Length);
                    Assert.All(rings, ring =>
                    {
                        Assert.Equal("Generating", ring.State);
                        Assert.Equal(21, ring.Width); Assert.Equal(21, ring.Height);
                        Assert.True(ring.RenderTransform.HasAnimatedProperties);
                    });
                    Assert.False(window.ExportButton.IsEnabled);
                    Assert.DoesNotContain(model.SubmissionItems, item => item.HasError);
                    Save(window, failSecond ? "error-pending" : "pending");
                    runner.Release(0);
                    await Until(() => model.SubmissionItems[0].State == ExportFilePreflightState.Ready);
                    Assert.Equal("Analyzing videos… 1 of 2 ready", window.ReadySummaryText.Text);
                    Assert.False(window.ExportButton.IsEnabled);
                    Assert.False(model.SubmissionItems[0].HasIssues);
                    Save(window, failSecond ? "error-one-ready" : "one-ready");
                    window.CodecCombo.SelectedIndex = 1; // Settings change while the second result is outstanding.
                    runner.Release(1);
                    await Until(() => !model.IsAnalyzing);
                    Assert.Equal(failSecond ? "1 of 2 videos needs attention" : "2 videos ready to export", window.ReadySummaryText.Text);
                    Assert.Equal(!failSecond, window.ExportButton.IsEnabled);
                    Assert.Empty(model.Warnings);
                    Assert.DoesNotContain("Saved exclusive source EOF", window.ValidationText.Text);
                    Assert.Equal(failSecond, model.SubmissionItems[1].HasError);
                    Assert.Equal(ExportCodecChoice.H264, model.Codec);
                    Assert.Equal(2, runner.Entered); // No repeated source discovery on an export-setting edit.
                    if (!failSecond) Assert.All(model.SubmissionItems, item => Assert.Equal(ExportFilePreflightState.Ready, item.State));
                    Save(window, failSecond ? "needs-attention" : "ready");
                }
                finally { heartbeat.Stop(); window.Close(); }
            });
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task Until(Func<bool> condition, int timeoutMs = 10000,
        string diagnostic = "Preflight state did not settle.")
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline, diagnostic); await Task.Delay(10); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Save(ExportDialog window, string phase)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bounds = VisualTreeHelper.GetDescendantBounds(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Math.Max(window.ActualWidth, bounds.Right) * dpi.DpiScaleX),
            (int)Math.Ceiling(Math.Max(window.ActualHeight, bounds.Bottom) * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        var directory = Path.Combine(repo!.FullName, "artifacts", "i03-ui"); Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, phase + ".png")); encoder.Save(output);
    }

    private sealed class ControlledRunner(bool failSecond) : IProbeProcessRunner
    {
        private readonly TaskCompletionSource[] _gates = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        private int _entered;
        public int Entered => Volatile.Read(ref _entered);
        public void Release(int index) => _gates[index].TrySetResult();
        public async Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            var index = Path.GetFileName(arguments[^1]).Contains("-0") ? 0 : 1;
            if (arguments.Contains("-show_packets")) return new(0, """{"packets":[{"pts_time":"0.04"}]}""", "");
            if (arguments.Any(argument => argument.StartsWith("frame=")))
                return new(0, failSecond && index == 1
                    ? """{"frames":[{"best_effort_timestamp_time":"0.16"}]}"""
                    : """{"frames":[{"best_effort_timestamp_time":"0.04","duration_time":"0.04"},{"best_effort_timestamp_time":"0.16","duration_time":"0.04"}]}""", "");
            Interlocked.Increment(ref _entered);
            await _gates[index].Task.WaitAsync(cancellationToken);
            return new(0, """{"streams":[{"codec_type":"video","codec_name":"hevc","width":256,"height":256,"avg_frame_rate":"25/1","duration":"0.2","time_base":"1/1000","start_time":"0"}],"format":{"format_name":"mp4","duration":"0.2","start_time":"0"}}""", "");
        }
    }
}
