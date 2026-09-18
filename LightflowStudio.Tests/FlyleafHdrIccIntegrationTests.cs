using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaRenderer;
using Vortice.Direct3D11;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class FlyleafPostProcessIntegrationTests
{
    [Theory]
    [InlineData("smpte2084", false)]
    [InlineData("arib-std-b67", false)]
    [InlineData("icc", false)]
    [InlineData("smpte2084", true)]
    [InlineData("icc", true)]
    public async Task HdrAndIcc_RestoreIntermediateAndBindingsAcrossLiveSnapshotSeekAndDeviceLifetimes(string transfer, bool failOpen)
    {
        var dependencies = RequireDependencies();
        var fixture = Path.Combine(_root, transfer == "icc" ? "profile.png" : transfer + ".mkv");
        if (transfer == "icc") GenerateIccFixture(fixture);
        else
        {
            var info = new ProcessStartInfo(Path.Combine(dependencies, "ffmpeg.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-v", "error", "-y", "-f", "lavfi", "-i",
                "smptebars=size=160x90:rate=10:duration=3", "-vf",
                "format=yuv420p10le,setparams=color_primaries=bt2020:color_trc=" + transfer + ":colorspace=bt2020nc",
                "-c:v", "ffv1", fixture })
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, error);
        }

        var probe = new RendererStateProbeFactory(failOpen);
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            // Compare actual displayed pixels with upstream's no-factory rendering path.
            var baseline = await CaptureLiveSurfaceAsync(dependencies, fixture, new DisabledProcessorFactory(), VideoProcessors.Flyleaf);
            var processed = await CaptureLiveSurfaceAsync(dependencies, fixture, probe, VideoProcessors.Flyleaf);
            Assert.True(ChannelRange(processed) > 32, "HDR/ICC intermediate lost the converted video.");
            Assert.True(MeanAbsoluteDifference(baseline, processed) < 3, "Post-processing changed the upstream HDR/ICC result without a LUT.");

            await using var backend = new FlyleafPlaybackBackend(dependencies, postProcessorFactory: probe,
                videoProcessor: VideoProcessors.Flyleaf);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            for (var lifetime = 0; lifetime < 2; lifetime++)
            {
                await backend.OpenAsync(fixture, timeout.Token);
                var host = Assert.IsType<FlyleafHost>(backend.CreatePresentationSurface());
                var window = new System.Windows.Window { Content = host, Width = 480, Height = 300,
                    ShowActivated = false, ShowInTaskbar = false };
                window.Show();
                try
                {
                    var renderer = host.Player.Renderer;
                    if (transfer == "icc")
                        Assert.Contains("dICC", ReadField<List<string>>(renderer, "defines"));
                    else if (transfer == "arib-std-b67")
                        Assert.Contains("dHLG", ReadField<List<string>>(renderer, "defines"));
                    else if (transfer == "smpte2084")
                    {
                        Assert.True(ReadField<bool>(renderer, "isHdr"), "Shader: " + ReadField<string>(renderer, "psId") + "; defines: " + string.Join(",", ReadField<List<string>>(renderer, "defines")));
                        Assert.NotNull(ReadField<object>(renderer, "psHdr"));
                    }
                    var first = await backend.CapturePresentedFrameAsync(timeout.Token);
                    Assert.True(first.BgraPixels.Distinct().Count() > 16);
                    for (var frame = 0; frame < 4; frame++)
                    {
                        var before = probe.LiveCalls;
                        backend.RequestRender();
                        await WaitUntilAsync(() => probe.LiveCalls > before, "HDR/ICC paused redraw");
                        var next = await backend.CapturePresentedFrameAsync(timeout.Token);
                        Assert.Equal(first.BgraPixels, next.BgraPixels);
                    }
                    if (transfer != "icc")
                    {
                        if (transfer == "smpte2084") Assert.True(ReadField<bool>(renderer, "hdrHasStats"), "Upstream luminance analysis did not run.");
                        await backend.StepForwardAsync(timeout.Token);
                        await backend.StepBackwardAsync(timeout.Token);
                        await backend.SeekAsync(TimeSpan.FromSeconds(1), timeout.Token);
                        var afterSeek = await backend.CapturePresentedFrameAsync(timeout.Token);
                        Assert.Equal(first.BgraPixels, afterSeek.BgraPixels);
                    }
                }
                finally { window.Content = null; backend.ReleasePresentationSurface(host); window.Close(); }
            }
        });
        Assert.Empty(probe.Failures);
        Assert.True(probe.LiveCalls > 8);
        Assert.True(probe.SnapshotCalls >= 10);
        Assert.True(probe.Created >= 3);
        Assert.Equal(probe.Created, probe.Disposed);
    }

    private static T ReadField<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void GenerateIccFixture(string path)
    {
        // WPF supplies a real embedded sRGB profile, so this is portable across Windows machines.
        var pixels = new byte[160 * 90 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = (byte)(index / 4 % 160);
            pixels[index + 1] = (byte)(index / 640 * 2);
            pixels[index + 2] = (byte)(255 - pixels[index]);
            pixels[index + 3] = 255;
        }
        var bitmap = BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgra32, null, pixels, 640);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, null,
            new System.Collections.ObjectModel.ReadOnlyCollection<ColorContext>([new(PixelFormats.Bgra32)])));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

internal sealed class RendererStateProbeFactory(bool failOpen) : IVideoPostProcessorFactory
{
    public readonly ConcurrentQueue<string> Failures = new();
    public int Created, Disposed, LiveCalls, SnapshotCalls;
    public IVideoPostProcessor Create(ID3D11Device device)
    {
        Interlocked.Increment(ref Created);
        return new Probe(this, new LightflowColorPostProcessorFactory().Create(device), failOpen);
    }

    private sealed class Probe(RendererStateProbeFactory owner, IVideoPostProcessor inner, bool failOpen) : IVideoPostProcessor
    {
        public void Process(in VideoPostProcessContext frame)
        {
            // Inspect real GPU bindings before the extension changes them. Assertions are deferred
            // because Flyleaf deliberately catches processor exceptions to fail open.
            var resources = new ID3D11ShaderResourceView[1];
            frame.DeviceContext.PSGetShaderResources(4, 1, resources);
            if (resources[0] is null) owner.Failures.Enqueue("Upstream ICC t4 binding was lost.");
            resources[0]?.Dispose();
            var targets = new ID3D11RenderTargetView[1];
            frame.DeviceContext.OMGetRenderTargets(1, targets, out var depth);
            using (depth)
            using (var target = targets[0])
            using (var actual = target?.Resource)
            using (var expected = frame.Input.Resource)
                if (actual is null || actual.NativePointer != expected.NativePointer)
                    owner.Failures.Enqueue("HDR analysis escaped the live/snapshot intermediate.");
            inner.Process(frame);
            // Exercise successful-extension cleanup too, not only the throwing fallback.
            frame.DeviceContext.PSSetShaderResource(4, null!);
            if (frame.IsSnapshot) Interlocked.Increment(ref owner.SnapshotCalls);
            else Interlocked.Increment(ref owner.LiveCalls);
            if (failOpen)
            {
                frame.DeviceContext.ClearState();
                throw new InvalidOperationException("Deliberate HDR/ICC processor failure.");
            }
        }
        public void Dispose() { inner.Dispose(); Interlocked.Increment(ref owner.Disposed); }
    }
}
