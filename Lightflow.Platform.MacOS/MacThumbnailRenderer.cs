using System.Runtime.InteropServices;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

public sealed record MacThumbnailObservation(string Source, double RequestedSeconds, long ActualValue,
    int ActualTimescale, double DurationSeconds, int Width, int Height, string Mechanism, int Status);

/// <summary>Native mechanisms only; shared services own identity, retry, lifecycle and poster selection.</summary>
public sealed class MacThumbnailRenderer(IThumbnailImageCodec codec, Action<MacThumbnailObservation>? observed = null)
    : IThumbnailRenderer, IMediaMetadataProbe
{
    // Host-specific cache identity refreshes old duration-only/unsupported records on normal demand.
    public static int MetadataProbeVersion(string? type) => 3;
    [StructLayout(LayoutKind.Sequential)]
    private struct Info
    {
        public double Duration; public int Width, Height, Orientation, Mechanism;
        public long ActualValue; public int ActualScale;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTrace
    {
        public double OpenMs, InitializationMs, MetadataMs, FrameMs, PixelsMs, ReadMs, TotalMs;
        public long BytesRead, ReadCalls, Requests;
        public int ActiveReaders, MetadataStatus;
        public long ErrorCode;
    }
    [DllImport("libLightflowStorage.dylib")]
    private static extern int lf_thumbnail_profile([MarshalAs(UnmanagedType.LPUTF8Str)] string source, int mode, double position,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, IntPtr cancelled, out Info info, out NativeTrace trace);
    private const string Library = "libLightflowStorage.dylib";
    [DllImport(Library)]
    private static extern int lf_thumbnail([MarshalAs(UnmanagedType.LPUTF8Str)] string source, int mode, double requested,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, IntPtr cancelled, out Info info);
    [DllImport(Library)] private static extern int lf_preview_begin();
    [DllImport(Library)] private static extern int lf_preview_end(int old);
    [DllImport(Library)] private static extern int lf_preview_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library)] private static extern void lf_preview_close(int descriptor);
    [DllImport(Library)] public static extern int lf_preview_readers();

    public bool Validate(string path) => codec.Validate(path);
    public Task<ThumbnailRenderResult> RenderAsync(string source, string type, TimeSpan position, string destination, CancellationToken token = default) =>
        RenderAsync(source, type, position, destination, ThumbnailColorRender.Original, token);
    public Task<ThumbnailRenderResult> RenderAsync(string source, string type, TimeSpan position, string destination, ThumbnailColorRender color, CancellationToken token = default) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (type is not ("image" or "raw-image" or "video")) return new ThumbnailRenderResult(ThumbnailGenerationStatus.Unsupported, "No thumbnail decoder is registered for this media type.");
            var intermediate = destination + ".native.png";
            try
            {
                if (type == "image" && codec.Supports(Path.GetExtension(source)))
                {
                    var descriptor = lf_preview_open(source);
                    if (descriptor < 0) return new(ThumbnailGenerationStatus.SourceMissing, "The source is not safely resident or readable.", PreviewFailureReason.SourceUnreadable);
                    try
                    {
                        var old = lf_preview_begin(); if (old < 0) throw new IOException("Could not disable cloud materialization for image decoding.");
                        try { return codec.Render($"/dev/fd/{descriptor}", destination, color.OrderedLutPaths, token); }
                        finally { if (lf_preview_end(old) != 0) throw new IOException("Could not restore source read policy."); }
                    }
                    finally { lf_preview_close(descriptor); }
                }
                var (status, info) = Native(source, type == "video" ? 2 : 1, position.TotalSeconds, intermediate, token);
                observed?.Invoke(new(source, position.TotalSeconds, info.ActualValue, info.ActualScale, info.Duration, info.Width, info.Height,
                    info.Mechanism switch { 1 => "embedded-preview", 2 => "native-image-render", 3 => "AVFoundation", _ => "unavailable" }, status));
                token.ThrowIfCancellationRequested();
                if (status != 0) return Failure(status);
                using var pixelTiming = ThumbnailPerformance.Measure("image.convert_resize_jpeg");
                return codec.Render(intermediate, destination, color.OrderedLutPaths, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            { return new(ThumbnailGenerationStatus.Failed, error.Message, PreviewFailureReason.SourceUnreadable); }
            finally { try { File.Delete(intermediate); } catch (IOException) { } }
        }, token);

    public Task<MediaProbeResult> ProbeAsync(string source, string type, long size, CancellationToken token = default) => Task.Run(() =>
    {
        if (type is not ("image" or "raw-image" or "video")) return new MediaProbeResult(DerivedMetadataStatus.Unsupported);
        var (status, info) = Native(source, type == "video" ? 0 : 3, 0, "", token); token.ThrowIfCancellationRequested();
        if (status != 0) return new MediaProbeResult(status == 2 ? DerivedMetadataStatus.SourceMissing : DerivedMetadataStatus.Unsupported, Diagnostic: Failure(status).Diagnostic);
        var video = type == "video" && info.Width > 0 && info.Height > 0
            ? new DerivedVideoMetadata("", null, info.Width, info.Height, null, null, null, null, null, null) : null;
        var image = type != "video" && info.Width > 0 && info.Height > 0
            ? new DerivedImageMetadata(Path.GetExtension(source), info.Width, info.Height, null, info.Orientation > 0 ? info.Orientation : null, null, null, null, null) : null;
        return new MediaProbeResult(DerivedMetadataStatus.Succeeded, new(type == "video" ? DerivedMediaKind.Video : DerivedMediaKind.Image,
            Path.GetExtension(source), info.Duration > 0 ? info.Duration : null, null, size, null, video, null, image));
    }, token);
    private static (int, Info) Native(string source, int mode, double position, string destination, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var flag = Marshal.AllocHGlobal(sizeof(int)); Marshal.WriteInt32(flag, 0);
        try
        {
            using var registration = timeout.Token.Register(() => Marshal.WriteInt32(flag, 1));
            // The slot stays owned until native callbacks drain, even after cancellation/timeout.
            using var timing = ThumbnailPerformance.Measure(mode == 0 ? "native.metadata" : "native.poster");
            Info info; int status;
            if (timing is not null)
            {
                status = lf_thumbnail_profile(source, mode, position, destination, flag, out info, out var trace);
                timing.SetTag("native.open.ms", trace.OpenMs); timing.SetTag("native.initialize.ms", trace.InitializationMs);
                timing.SetTag("native.metadata.ms", trace.MetadataMs); timing.SetTag("native.seek_decode.ms", trace.FrameMs);
                timing.SetTag("native.convert_resize_write.ms", trace.PixelsMs); timing.SetTag("native.read.ms", trace.ReadMs);
                timing.SetTag("native.bytes", trace.BytesRead); timing.SetTag("native.reads", trace.ReadCalls); timing.SetTag("native.requests", trace.Requests);
                timing.SetTag("native.active", trace.ActiveReaders); timing.SetTag("native.status", status); timing.SetTag("native.error", trace.ErrorCode);
                timing.SetTag("native.actual.value", info.ActualValue); timing.SetTag("native.actual.timescale", info.ActualScale);
            }
            else status = lf_thumbnail(source, mode, position, destination, flag, out info);
            token.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested) throw new IOException("Native thumbnail decoding timed out.");
            return (status, info);
        }
        finally { Marshal.FreeHGlobal(flag); }
    }
    private static ThumbnailRenderResult Failure(int status) => status switch
    {
        1 => new(ThumbnailGenerationStatus.Failed, "Native thumbnail operation was cancelled."),
        2 => new(ThumbnailGenerationStatus.SourceMissing, "The source is not safely resident or readable.", PreviewFailureReason.SourceUnreadable),
        3 => new(ThumbnailGenerationStatus.Unsupported, "This media format or codec is unavailable in the native decoder.", PreviewFailureReason.CodecUnavailable),
        _ => new(ThumbnailGenerationStatus.InvalidOutput, "The native decoder could not produce a thumbnail.")
    };
}
