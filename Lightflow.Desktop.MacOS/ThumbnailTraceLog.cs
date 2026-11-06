using System.Diagnostics;
using System.Text.Json;
using LightflowStudio;
namespace Lightflow.Desktop.MacOS;

/// <summary>Explicit task-profile diagnostics only; no source paths, filenames or raw metadata.</summary>
internal sealed class ThumbnailTraceLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly ActivityListener _listener;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _sync = new();
    private ThumbnailTraceLog(string path)
    {
        _writer = new(path, false) { AutoFlush = true };
        _listener = new()
        {
            ShouldListenTo = s => s.Name is "LightflowStudio.Browser" or "LightflowStudio.Thumbnails",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { try { lock (_sync) _writer.WriteLine(JsonSerializer.Serialize(new { stage = a.OperationName, atMs = _clock.Elapsed.TotalMilliseconds, elapsedMs = a.Duration.TotalMilliseconds, traceId = a.TraceId.ToHexString(), spanId = a.SpanId.ToHexString(), parentId = a.ParentSpanId.ToHexString(), tags = a.TagObjects.ToDictionary(t => t.Key, t => t.Value) })); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException) { } }
        };
        ActivitySource.AddActivityListener(_listener);
    }
    public static ThumbnailTraceLog? Create(string dataRoot)
    {
        if (Environment.GetEnvironmentVariable("LIGHTFLOW_THUMBNAIL_TRACE") != "1") return null;
        try { Directory.CreateDirectory(dataRoot); return new(Path.Combine(dataRoot, "thumbnail-trace.jsonl")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("Thumbnail diagnostics unavailable."); return null; }
    }
    public void Dispose() { _listener.Dispose(); lock (_sync) _writer.Dispose(); }
}
