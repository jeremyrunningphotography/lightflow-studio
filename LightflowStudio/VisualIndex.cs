using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

internal static class VisualIndexSampling
{
    internal static readonly int[] Counts = [12, 24, 48];
    internal static int NormalizeCount(int count) => Counts.Contains(count) ? count : 24;
    internal static IReadOnlyList<TimeSpan> PlanAll(TimeSpan? duration, double frameRate) =>
        Counts.SelectMany(count => Plan(duration, frameRate, count)).Distinct().Order().ToArray();

    // Nominal frame slots, not decoded PTS. Keep a complete frame interval before EOF.
    internal static IReadOnlyList<TimeSpan> Plan(TimeSpan? duration, double frameRate, int count)
    {
        if (duration is null || duration <= TimeSpan.Zero) return [];
        count = NormalizeCount(count);
        var cadence = double.IsFinite(frameRate) && frameRate > 0 ? frameRate : 25;
        var step = Math.Max(1L, (long)Math.Min(long.MaxValue, Math.Ceiling(TimeSpan.TicksPerSecond / cadence)));
        var lastSlot = Math.Max(0, duration.Value.Ticks / step - 1);
        var actual = (int)Math.Min(count, lastSlot + 1);
        return Enumerable.Range(0, actual).Select(i => TimeSpan.FromTicks(actual == 1 ? 0 :
            (long)((decimal)lastSlot * i / (actual - 1)) * step)).ToArray();
    }
    internal static int Columns(double width) => double.IsFinite(width) ? Math.Clamp((int)(width / 144), 1, 3) : 1;
}

internal sealed class VisualIndexCard(TimeSpan position) : INotifyPropertyChanged
{
    public TimeSpan Position { get; } = position;
    public string Timestamp => $"{(long)Position.TotalHours:00}:{Position.Minutes:00}:{Position.Seconds:00}.{Position.Milliseconds:000}";
    public string NavigationLabel => $"Seek to {Position:c}";
    public BitmapSource? Frame { get; private set; }
    internal bool Finished { get; private set; }
    public string Status { get; private set; } = "Generating…";
    public bool IsCurrent { get; private set; }
    public string CurrentLabel => IsCurrent ? "Current" : "";
    internal void Reset()
    {
        Frame = null; Finished = false; Status = "Generating…";
        Changed(nameof(Frame)); Changed(nameof(Status));
    }
    internal void SetCurrent(bool value)
    {
        if (IsCurrent == value) return;
        IsCurrent = value; Changed(nameof(IsCurrent)); Changed(nameof(CurrentLabel));
    }
    internal void Publish(BitmapSource? frame)
    {
        Finished = true; Frame = frame; Status = frame is null ? "Unavailable" : "";
        Changed(nameof(Frame)); Changed(nameof(Status));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>UI-context projection; decoding is bounded by the shared frame demand scheduler.</summary>
internal sealed class VisualIndexModel(IPositionFrameService frames) : IDisposable
{
    private CancellationTokenSource? _work;
    private long _generation;
    private Guid? _assetId;
    private TimeSpan? _duration;
    private double _frameRate;
    private TimeSpan _position;
    private long _revision;
    private bool _generating;
    internal string Status => _generating ? $"Generating {Cards.Count(card => card.Frame is not null)} of {Cards.Count}…"
        : Cards.Any(card => card.Finished && card.Frame is null) ? "Some frames are unavailable. Reopen Visual Index to retry." : "";
    internal event EventHandler? ProgressChanged;
    internal int Count { get; private set; } = 24;
    internal IReadOnlyList<VisualIndexCard> Cards { get; private set; } = [];
    internal event EventHandler? Changed;
    internal Task Pending { get; private set; } = Task.CompletedTask;
    internal void SetContext(Guid? assetId, TimeSpan? duration, double frameRate, int count, bool active, long revision = 0)
    {
        count = VisualIndexSampling.NormalizeCount(count);
        frameRate = double.IsFinite(frameRate) && frameRate > 0 ? frameRate : 0;
        if (_revision == revision && _assetId == assetId && _duration == duration && _frameRate == frameRate && Count == count &&
            active == (_work is not null)) return;
        var changedPlan = _revision != revision || _assetId != assetId || _duration != duration || _frameRate != frameRate || Count != count;
        Cancel();
        _revision = revision; _assetId = assetId; _duration = duration; _frameRate = frameRate; Count = count;
        if (changedPlan)
            Cards = assetId is null ? [] : VisualIndexSampling.Plan(duration, frameRate, count).Select(p => new VisualIndexCard(p)).ToArray();
        UpdatePosition(_position);
        if (changedPlan) Changed?.Invoke(this, EventArgs.Empty);
        if (!active || assetId is null) return;
        _work = new();
        _generating = Cards.Count > 0;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
        Pending = Cards.Count == 0 ? Task.CompletedTask : GenerateAsync(assetId.Value, Cards, _generation, _work.Token);
    }
    internal void UpdatePosition(TimeSpan position)
    {
        _position = position;
        var nearest = Cards.MinBy(c => Math.Abs((decimal)c.Position.Ticks - position.Ticks));
        foreach (var card in Cards) card.SetCurrent(ReferenceEquals(card, nearest));
    }
    internal bool Contains(Guid assetId, VisualIndexCard card) => _assetId == assetId && Cards.Contains(card);
    private async Task GenerateAsync(Guid assetId, IReadOnlyList<VisualIndexCard> cards, long generation, CancellationToken token)
    {
        try
        {
            // Even synchronous cache/fixture implementations must not perform IO on the dispatcher.
            var context = await Task.Run(() => frames.PrepareContextAsync(assetId, token), token);
            async Task PublishAsync(VisualIndexCard card, bool cacheOnly)
            {
                token.ThrowIfCancellationRequested();
                BitmapSource? bitmap = null;
                try
                {
                    bitmap = await Task.Run(async () =>
                    {
                        var path = cacheOnly
                            ? await frames.FindCachedAsync(context, card.Position, token).ConfigureAwait(false)
                            : await frames.GetAsync(context, card.Position, ThumbnailPriority.Visible, token).ConfigureAwait(false);
                        return path is null ? null : PlayerViewerHost.DecodeImage(path);
                    }, token);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* One failed derivative must not suppress the remaining samples. */ }
                if (generation != _generation || token.IsCancellationRequested) return;
                if (bitmap is not null || !cacheOnly) card.Publish(bitmap);
                else card.Reset();
                ProgressChanged?.Invoke(this, EventArgs.Empty);
            }
            // Expose every cache hit before scheduling any missing decode.
            await Task.WhenAll(cards.Select(card => PublishAsync(card, true)));
            await Task.WhenAll(cards.Where(card => card.Frame is null).Select(card => PublishAsync(card, false)));
            if (!await frames.IsCurrentAsync(context, token) && generation == _generation)
                foreach (var card in cards) card.Publish(null);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (generation == _generation && !token.IsCancellationRequested)
                foreach (var card in cards) card.Publish(null);
        }
        finally
        {
            if (generation == _generation)
            {
                _generating = false;
                ProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    private void Cancel() { ++_generation; _work?.Cancel(); _work?.Dispose(); _work = null; _generating = false; }
    public void Dispose() => Cancel();
}
