using System.Diagnostics;
using System.Text;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using LightflowStudio;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class FlyleafPostProcessIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-post-process-").FullName;

    [Fact]
    public async Task FlyleafVp_LivePausedInvalidationSnapshotStepSwitchAndDisposalUseOneDeviceScopedProcessorPerPlayer()
    {
        var dependencies = RequireDependencies();
        var first = Path.Combine(_root, "first.mkv");
        var second = Path.Combine(_root, "second.mkv");
        GenerateFixture(Path.Combine(dependencies, "ffmpeg.exe"), first, "ffv1");
        GenerateFixture(Path.Combine(dependencies, "ffmpeg.exe"), second, "ffv1");
        var factory = new RecordingProcessorFactory();

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies, postProcessorFactory: factory,
                videoProcessor: VideoProcessors.Flyleaf);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await backend.OpenAsync(first, timeout.Token);
            using var presentation = new MediaPlaybackPresentation(backend.CreatePresentationSurface(),
                backend.ReleasePresentationSurface, backend.CapturePresentedFrameAsync);
            var window = new System.Windows.Window
            {
                Content = presentation.Surface, Width = 320, Height = 240,
                ShowActivated = false, ShowInTaskbar = false
            };
            window.Show();
            try
            {
                await WaitUntilAsync(() => factory.LiveCalls > 0, "a live FlyleafVP post-process call");
                Assert.Equal(VideoProcessors.Flyleaf, backend.ActiveVideoProcessor);

                var beforeInvalidation = factory.LiveCalls;
                backend.RequestRender();
                await WaitUntilAsync(() => factory.LiveCalls > beforeInvalidation, "paused render invalidation");

                var snapshot = await backend.CapturePresentedFrameAsync(timeout.Token);
                Assert.NotEmpty(snapshot.BgraPixels);
                Assert.True(factory.SnapshotCalls > 0);
                await backend.StepForwardAsync(timeout.Token);
                await backend.StepBackwardAsync(timeout.Token);

                await backend.OpenAsync(second, timeout.Token);
                Assert.Equal(2, factory.Created);
                Assert.Equal(1, factory.Disposed);
                await WaitUntilAsync(() => factory.LiveCalls > beforeInvalidation + 1, "post-processing after source switch");
            }
            finally { window.Close(); }
        });

        Assert.Equal(factory.Created, factory.Disposed);
        Assert.Equal(0, factory.CallsAfterDisposal);
    }

    [Fact]
    public async Task D3D11Vp_ProcessesLiveAndSnapshotWhenHardwarePathIsAvailable()
    {
        var dependencies = RequireDependencies();
        var fixture = Path.Combine(_root, "hardware.mp4");
        GenerateFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, "libopenh264");
        var factory = new RecordingProcessorFactory();
        var validated = false;

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies, postProcessorFactory: factory,
                videoProcessor: VideoProcessors.D3D11);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var opened = await backend.OpenAsync(fixture, timeout.Token);
            if (!opened.Source.UsesHardwareDecode || backend.ActiveVideoProcessor != VideoProcessors.D3D11)
                return; // Hosted/virtual GPUs legitimately cannot expose the D3D11 video-processor path.
            validated = true;

            var surface = backend.CreatePresentationSurface();
            var window = new System.Windows.Window { Content = surface, Width = 320, Height = 240, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                await WaitUntilAsync(() => factory.LiveCalls > 0, "a live D3D11VP post-process call");
                var snapshot = await backend.CapturePresentedFrameAsync(timeout.Token);
                Assert.NotEmpty(snapshot.BgraPixels);
                Assert.True(factory.SnapshotCalls > 0);
            }
            finally
            {
                window.Content = null;
                backend.ReleasePresentationSurface(surface);
                window.Close();
            }
        });
        Assert.Equal(factory.Created, factory.Disposed);
        if (Environment.GetEnvironmentVariable("LIGHTFLOW_REQUIRE_D3D11VP") == "1")
            Assert.True(validated, "This machine did not expose hardware decode plus D3D11VP; required hardware-path validation could not run.");
    }

    [Fact]
    public async Task ProcessorExceptionFailsOpenForSnapshotAndDisposesCleanly()
    {
        var dependencies = RequireDependencies();
        var fixture = Path.Combine(_root, "throwing.mkv");
        GenerateFixture(Path.Combine(dependencies, "ffmpeg.exe"), fixture, "ffv1");
        var factory = new RecordingProcessorFactory(throwOnProcess: true);

        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            await using var backend = new FlyleafPlaybackBackend(dependencies, postProcessorFactory: factory,
                videoProcessor: VideoProcessors.Flyleaf);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await backend.OpenAsync(fixture, timeout.Token);
            var surface = backend.CreatePresentationSurface();
            var window = new System.Windows.Window { Content = surface, Width = 320, Height = 240, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                var frame = await backend.CapturePresentedFrameAsync(timeout.Token);
                Assert.NotEmpty(frame.BgraPixels);
                Assert.True(factory.Attempts > 0);
            }
            finally
            {
                window.Content = null;
                backend.ReleasePresentationSurface(surface);
                window.Close();
            }
        });

        Assert.Equal(factory.Created, factory.Disposed);
        Assert.Equal(0, factory.CallsAfterDisposal);
    }

    private string RequireDependencies() => PlaybackDependencyLocator.FindSharedLibraries()
        ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");

    private static void GenerateFixture(string ffmpeg, string output, string codec)
    {
        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
            "testsrc2=size=160x90:rate=10:duration=2", "-an", "-c:v", codec, output
        }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start FFmpeg.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(predicate(), $"Timed out waiting for {description}.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}

internal sealed class RecordingProcessorFactory : IVideoPostProcessorFactory
{
    private readonly bool _throwOnProcess;
    public int Created;
    public int Disposed;
    public int Attempts;
    public int LiveCalls;
    public int SnapshotCalls;
    public int CallsAfterDisposal;

    public RecordingProcessorFactory(bool throwOnProcess = false) => _throwOnProcess = throwOnProcess;

    public IVideoPostProcessor Create(ID3D11Device device)
    {
        Interlocked.Increment(ref Created);
        return new RecordingProcessor(this, device, _throwOnProcess);
    }

    private sealed class RecordingProcessor : IVideoPostProcessor
    {
        private const string VertexShader = """
            struct O { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            O main(uint id : SV_VertexID) {
                O o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.uv = float2(p.x, 1.0 - p.y);
                o.position = float4(p * float2(2, -2) + float2(-1, 1), 0, 1);
                return o;
            }
            """;
        private const string PixelShader = """
            Texture2D inputTexture : register(t0);
            SamplerState inputSampler : register(s0);
            float4 main(float4 position : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET {
                float4 value = inputTexture.Sample(inputSampler, uv);
                return float4(1.0 - value.rgb, value.a);
            }
            """;

        private readonly RecordingProcessorFactory _owner;
        private readonly bool _throwOnProcess;
        private readonly ID3D11VertexShader _vertexShader;
        private readonly ID3D11PixelShader _pixelShader;
        private readonly ID3D11SamplerState _sampler;
        private int _disposed;

        public RecordingProcessor(RecordingProcessorFactory owner, ID3D11Device device, bool throwOnProcess)
        {
            _owner = owner;
            _throwOnProcess = throwOnProcess;
            using var vs = Compile(VertexShader, "vs_5_0");
            using var ps = Compile(PixelShader, "ps_5_0");
            _vertexShader = device.CreateVertexShader(vs);
            _pixelShader = device.CreatePixelShader(ps);
            _sampler = device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MaxLOD = float.MaxValue
            });
        }

        public void Process(in VideoPostProcessContext frame)
        {
            Interlocked.Increment(ref _owner.Attempts);
            if (Volatile.Read(ref _disposed) != 0) Interlocked.Increment(ref _owner.CallsAfterDisposal);
            if (_throwOnProcess) throw new InvalidOperationException("Intentional post-process test failure.");
            if (frame.IsSnapshot) Interlocked.Increment(ref _owner.SnapshotCalls);
            else Interlocked.Increment(ref _owner.LiveCalls);

            var context = frame.DeviceContext;
            context.OMSetRenderTargets(frame.Output);
            context.RSSetViewport(new Viewport(frame.OutputWidth, frame.OutputHeight));
            context.IASetInputLayout(null);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vertexShader);
            context.PSSetShader(_pixelShader);
            context.PSSetSampler(0, _sampler);
            context.PSSetShaderResource(0, frame.Input);
            context.Draw(3, 0);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _sampler.Dispose();
            _pixelShader.Dispose();
            _vertexShader.Dispose();
            Interlocked.Increment(ref _owner.Disposed);
        }

        private static Blob Compile(string source, string target)
        {
            Compiler.Compile(Encoding.UTF8.GetBytes(source), null!, null!, "main", null!, target,
                ShaderFlags.OptimizationLevel3, out var shader, out var errors);
            using (errors) { }
            return shader;
        }
    }
}
