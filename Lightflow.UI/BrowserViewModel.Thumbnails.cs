using Avalonia.Threading;
using LightflowStudio;
namespace Lightflow.UI;

public sealed partial class BrowserViewModel
{
    private CancellationTokenSource? _thumbnailDemand;
    private long _thumbnailGeneration;
    private IThumbnailGenerationActivity? _thumbnailActivity;
    public bool IsPreviewGenerating(Guid id) => _thumbnailActivity?.IsGenerating(id) == true;
    private void PreviewActivityChanged(object? sender, ThumbnailGenerationActivityChanged change) =>
        Dispatcher.UIThread.Post(() => { if (_thumbnails.ContainsKey(change.AssetId)) OnThumbnailChanged(); });
    private readonly Dictionary<Guid, BrowserThumbnail> _thumbnails = [];
    public IReadOnlyDictionary<Guid, BrowserThumbnail> Thumbnails => _thumbnails;
    public long ThumbnailScopeRevision => _generation;
    public int ThumbnailDemandCount => _thumbnails.Count;
    public void ClearThumbnailDemand()
    {
        _thumbnailGeneration++; _thumbnailDemand?.Cancel(); _thumbnailDemand?.Dispose(); _thumbnailDemand = null;
        if (_thumbnailActivity is not null) _thumbnailActivity.Changed -= PreviewActivityChanged;
        _thumbnailActivity = null;
        _thumbnails.Clear(); OnThumbnailChanged();
    }
    private void OnThumbnailChanged() => PropertyChanged?.Invoke(this, new(nameof(Thumbnails)));
    public async Task SetThumbnailViewportAsync(IReadOnlyList<Guid> visible, IReadOnlyList<Guid> overscan)
    {
        if (session is not IBrowserThumbnailSession previews || !IsAvailable || IsLoading || IsProvisional) return;
        var ids = visible.Concat(overscan).Distinct().Take(192).ToArray();
        var prior = _thumbnailDemand;
        var demand = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _thumbnailDemand = demand;
        var token = demand.Token; var generation = ++_thumbnailGeneration; var scope = _generation; var epoch = _epoch;
        bool Current() => generation == _thumbnailGeneration && scope == _generation && epoch == _epoch && !token.IsCancellationRequested;
        foreach (var id in _thumbnails.Keys.Except(ids).ToArray()) _thumbnails.Remove(id);
        foreach (var id in ids) if (!_thumbnails.ContainsKey(id)) _thumbnails[id] = new(id, null, "Loading");
        OnThumbnailChanged();
        try
        {
            var first = await previews.DemandThumbnailsAsync(visible.Where(ids.Contains).Distinct().ToArray(), DerivedWorkPriority.Visible, token);
            var second = await previews.DemandThumbnailsAsync(overscan.Where(id => ids.Contains(id) && !visible.Contains(id)).Distinct().ToArray(), DerivedWorkPriority.Normal, token);
            if (!Current()) { first?.Cancel(); second?.Cancel(); return; }
            prior?.Cancel(); prior?.Dispose(); prior = null;
            if (!ReferenceEquals(_thumbnailActivity, previews.ThumbnailActivity))
            {
                if (_thumbnailActivity is not null) _thumbnailActivity.Changed -= PreviewActivityChanged;
                _thumbnailActivity = previews.ThumbnailActivity;
                if (_thumbnailActivity is not null) _thumbnailActivity.Changed += PreviewActivityChanged;
            }
            var reading = false; var again = false;
            async Task Refresh()
            {
                if (!Current()) return;
                if (reading) { again = true; return; }
                reading = true;
                try
                {
                    do
                    {
                        again = false; IReadOnlyDictionary<Guid, BrowserThumbnail> values;
                        using (ThumbnailPerformance.Measure("ui.cache.read")) values = await previews.ReadThumbnailsAsync(ids, token);
                        if (!Current()) return;
                        using var applyTiming = ThumbnailPerformance.Measure("ui.apply");
                        foreach (var (id, value) in values) if (ids.Contains(id)) _thumbnails[id] = value;
                        OnThumbnailChanged();
                    } while (again && Current());
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { if (Current()) { foreach (var id in ids) _thumbnails[id] = new(id, null, "Preview unavailable", Diagnostic: error.Message); OnThumbnailChanged(); } }
                finally { reading = false; }
            }
            async Task Observe(IDerivedWorkBatch batch)
            {
                EventHandler<DerivedWorkProgress> handler = (_, _) =>
                {
                    var postedAt = ThumbnailPerformance.Source.HasListeners() ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    Dispatcher.UIThread.Post(() =>
                    {
                        using var dispatch = ThumbnailPerformance.Measure("ui.dispatch");
                        if (postedAt != 0) dispatch?.SetTag("queue.wait.ms", System.Diagnostics.Stopwatch.GetElapsedTime(postedAt).TotalMilliseconds);
                        if (Current()) OnThumbnailChanged();
                        _ = Refresh();
                    });
                };
                batch.ProgressChanged += handler;
                try
                {
                    await batch.Completion; await Refresh();
                    if (Current()) foreach (var result in batch.Results)
                            if (result.Outcome == DerivedWorkItemOutcome.SkippedUnavailable && _thumbnails.TryGetValue(result.AssetId, out var current))
                                _thumbnails[result.AssetId] = current with { State = "Unavailable", Diagnostic = result.Diagnostic };
                    if (Current()) OnThumbnailChanged();
                }
                finally { batch.ProgressChanged -= handler; }
            }
            if (first is not null) _ = Observe(first); if (second is not null) _ = Observe(second);
            await Refresh();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current()) { foreach (var id in ids) _thumbnails[id] = new(id, null, "Preview unavailable", Diagnostic: error.Message); OnThumbnailChanged(); }
        }
        finally { prior?.Cancel(); prior?.Dispose(); }
    }

    private async Task HydrateNavigationCacheAsync(IReadOnlyList<MediaAsset> assets, bool provisional, long generation, CancellationToken token)
    {
        if (session is not IBrowserThumbnailSession previews) return;
        // A bounded initial page uses local Preview data only. No generation or source validation is
        // scheduled until authoritative discovery finishes. Scrolling remains free during that work.
        var ids = assets.Take(192).Select(asset => asset.AssetId).ToArray();
        using var timing = ThumbnailPerformance.Measure("ui.navigation.cache");
        IReadOnlyDictionary<Guid, BrowserThumbnail> values;
        try { values = await previews.ReadThumbnailsAsync(ids, token).WaitAsync(token); }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        { Console.Error.WriteLine($"Cached preview projection unavailable: {error.Message}"); values = new Dictionary<Guid, BrowserThumbnail>(); }
        if (generation != _generation || token.IsCancellationRequested) return;
        _thumbnails.Clear();
        foreach (var (id, value) in values)
            if (ids.Contains(id)) _thumbnails[id] = provisional ? value with
            { State = value.Path is null ? "Checking" : "Cached", Diagnostic = "Cached preview · source changes are being checked." } : value;
        OnThumbnailChanged();
    }

    private void MarkNavigationCacheUnverified()
    {
        foreach (var id in _thumbnails.Keys.ToArray()) _thumbnails[id] = _thumbnails[id] with
        { State = "Unverified", Diagnostic = "Source verification did not complete. Retry or reconnect the location." };
        SourceMessage = "Cached Catalog details · source availability has not been verified.";
        OnThumbnailChanged();
    }
}
