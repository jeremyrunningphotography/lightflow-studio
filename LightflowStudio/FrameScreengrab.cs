using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

internal sealed record FrameScreengrabResult(string Path, MediaPresentationTimestamp Timestamp, int Width, int Height);

internal interface IFrameScreengrabService
{
    Task<FrameScreengrabResult> SaveAsync(string sourcePath, MediaDecodedFrame frame,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes an already-decoded native frame to a lossless PNG. Frame acquisition remains owned by the shared
/// playback backend; this service deliberately knows nothing about Browser, WPF presentation size, or screen
/// coordinates, so every current or future Player host can reuse the same collision-safe output behavior.
/// </summary>
internal sealed class FrameScreengrabService(Func<string> outputDirectory) : IFrameScreengrabService
{
    public Task<FrameScreengrabResult> SaveAsync(string sourcePath, MediaDecodedFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Save(sourcePath, frame, cancellationToken), cancellationToken);
    }

    private FrameScreengrabResult Save(string sourcePath, MediaDecodedFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = outputDirectory();
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Choose a Screengrab folder in Settings before capturing a frame.");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var stem = BuildFileStem(sourcePath, frame.Timestamp.Position);
        for (var collision = 0; ; collision++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suffix = collision == 0 ? "" : $"-{collision:D3}";
            var path = Path.Combine(directory, stem + suffix + ".png");
            try
            {
                WritePng(path, frame);
                return new(path, frame.Timestamp, frame.Width, frame.Height);
            }
            catch (IOException) when (File.Exists(path))
            {
                // FileMode.CreateNew makes repeated/concurrent captures collision-safe without overwriting.
            }
        }
    }

    internal static string BuildFileStem(string sourcePath, TimeSpan position)
    {
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(sourceName.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        if (safeName.Length == 0) safeName = "frame";
        if (safeName.Length > 120) safeName = safeName[..120].TrimEnd();
        var ticks = Math.Max(0, position.Ticks);
        var settled = TimeSpan.FromTicks(ticks);
        var totalHours = (long)settled.TotalHours;
        return string.Create(CultureInfo.InvariantCulture,
            $"{safeName}_{totalHours:D2}-{settled.Minutes:D2}-{settled.Seconds:D2}.{settled.Milliseconds:D3}_t{ticks:D19}");
    }

    private static void WritePng(string path, MediaDecodedFrame frame)
    {
        // Acquire the collision-safe destination before entering cleanup ownership. If CreateNew fails because
        // another capture already owns this name, that existing file must never be treated as our partial.
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null,
                frame.BgraPixels, frame.Stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }
        catch
        {
            stream.Dispose();
            try { File.Delete(path); } catch { }
            throw;
        }
        finally { stream.Dispose(); }
    }
}
