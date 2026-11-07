using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LightflowStudio;
namespace Lightflow.UI;

public sealed partial class BrowserPane
{
    private string _viewportDemand = "";
    private IReadOnlyList<MediaAsset>? _viewportAssets;
    private Dictionary<Guid, int> _viewportIndices = [];
    private readonly DispatcherTimer _viewportDebounce = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private void InitializeThumbnailDemand()
    {
        _assets.LayoutUpdated += (_, _) => QueueViewport();
        _assets.DetachedFromVisualTree += (_, _) => { _viewportDebounce.Stop(); _viewportDemand = ""; _model.ClearThumbnailDemand(); };
        _viewportDebounce.Tick += async (_, _) => { _viewportDebounce.Stop(); await DemandViewportAsync(); };
    }
    private void QueueViewport()
    {
        if (_model.IsLoading) { _viewportDemand = ""; return; }
        var signature = Viewport().Signature;
        if (signature == _viewportDemand) return;
        _viewportDemand = signature; _viewportDebounce.Stop(); _viewportDebounce.Start();
    }
    private (string Signature, Guid[] Visible, Guid[] Overscan) Viewport()
    {
        var realized = _assets.GetVisualDescendants().OfType<BrowserGridCard>().ToArray();
        var visible = realized.Where(x => x.TranslatePoint(default, _assets) is { } top && top.Y + x.Bounds.Height >= 0 && top.Y < _assets.Bounds.Height)
            .Select(x => x.AssetId).ToArray();
        if (_presentation==BrowserLayoutMode.Details)
            visible=RealizedDetails.Where(x=>x.TranslatePoint(default,_assets) is { } top && top.Y+x.Bounds.Height>=0 && top.Y<_assets.Bounds.Height).Select(x=>x.AssetId).ToArray();
        var ids = visible.Distinct().Take(64).ToArray();
        if (ids.Length == 0) return ("", [], []);
        if (!ReferenceEquals(_viewportAssets, _model.Assets)) { _viewportAssets = _model.Assets; _viewportIndices = _model.Assets.Select((a, i) => (a.AssetId, i)).ToDictionary(x => x.AssetId, x => x.i); }
        var index = _viewportIndices.GetValueOrDefault(ids[0]);
        var start = Math.Max(0, index - ids.Length); var count = Math.Min(192, ids.Length * 3);
        var over = _model.Assets.Skip(start).Take(count).Select(x => x.AssetId).Except(ids).ToArray();
        return (_model.Epoch + ":" + _model.ThumbnailScopeRevision + ":" + string.Join(',', ids) + "|" + string.Join(',', over), ids, over);
    }
    private Task DemandViewportAsync()
    {
        var view = Viewport(); return _model.SetThumbnailViewportAsync(view.Visible, view.Overscan);
    }
}
