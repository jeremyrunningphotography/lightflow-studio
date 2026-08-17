using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;

namespace LightflowStudio;

/// <summary>True collapses; false (or anything else) shows. The inverse of the standard converter.</summary>
internal sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// One media tile in the Browser thumbnail grid. Identity is the stable, Catalog-normalized
/// <see cref="MediaFolderEntry.RelativePathKey"/> rather than any visual container, so selection and
/// thumbnail state survive recycling, reflow, and non-destructive folder refresh.
/// </summary>
internal sealed class BrowserGridTile : INotifyPropertyChanged
{
    private bool _isSelected;
    private string? _thumbnailPath;
    private int _index;

    public BrowserGridTile(MediaFolderEntry entry, int index)
    {
        RootId = entry.RootId;
        RelativePath = entry.RelativePath;
        Key = entry.RelativePathKey;
        Name = entry.Name;
        Category = entry.MediaType.Category;
        FileSizeBytes = entry.FileSizeBytes;
        _index = index;
    }

    public Guid RootId { get; }
    public string RelativePath { get; }
    public string Key { get; }
    public string Name { get; }
    public MediaTypeCategory Category { get; }
    public long? FileSizeBytes { get; }
    public Guid? AssetId { get; private set; }

    /// <summary>
    /// A tile only ever represents a supported still image, RAW image, or video asset — see
    /// <see cref="BrowserGridModel.IsPresentable"/>. This glyph set is therefore exhaustive.
    /// </summary>
    public string CategoryGlyph => Category switch
    {
        MediaTypeCategory.StillImage => "",
        MediaTypeCategory.RawImage => "",
        MediaTypeCategory.Video => "",
        _ => throw new InvalidOperationException($"{Category} is not a presentable Browser media category.")
    };

    public string AutomationLabel => $"{Name}, {Category} media";

    public int Index
    {
        get => _index;
        set { if (_index != value) { _index = value; OnPropertyChanged(); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    /// <summary>Absolute path of the generated Preview thumbnail, or null while a placeholder is shown.</summary>
    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (_thumbnailPath == value) return;
            _thumbnailPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool HasThumbnail => _thumbnailPath is not null;

    public void SetAssetId(Guid assetId)
    {
        if (AssetId == assetId) return;
        AssetId = assetId;
        OnPropertyChanged(nameof(AssetId));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A bounded-width row of tiles. Rows are the outer virtualization unit for the grid.</summary>
internal sealed class BrowserGridRow(IReadOnlyList<BrowserGridTile> tiles)
{
    public ObservableCollection<BrowserGridTile> Tiles { get; } = new(tiles);

    public void SetTiles(IReadOnlyList<BrowserGridTile> tiles)
    {
        for (var index = 0; index < tiles.Count; index++)
        {
            if (index < Tiles.Count) { if (!ReferenceEquals(Tiles[index], tiles[index])) Tiles[index] = tiles[index]; }
            else Tiles.Add(tiles[index]);
        }
        while (Tiles.Count > tiles.Count) Tiles.RemoveAt(Tiles.Count - 1);
    }
}

/// <summary>
/// Pure column/row arithmetic for the responsive thumbnail grid. Kept independent of WPF so reflow
/// behavior is directly unit-testable.
/// </summary>
internal static class BrowserGridLayout
{
    public const double TileWidth = 168;
    public const double TileSpacing = 12;

    public static int ComputeColumns(double availableWidth, double tileWidth = TileWidth, double spacing = TileSpacing)
    {
        if (tileWidth <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidth));
        if (availableWidth < tileWidth) return 1;
        return Math.Max(1, (int)((availableWidth + spacing) / (tileWidth + spacing)));
    }

    public static IReadOnlyList<IReadOnlyList<BrowserGridTile>> BuildRows(IReadOnlyList<BrowserGridTile> tiles, int columns)
    {
        columns = Math.Max(1, columns);
        var rows = new List<IReadOnlyList<BrowserGridTile>>();
        for (var index = 0; index < tiles.Count; index += columns)
            rows.Add(tiles.Skip(index).Take(columns).ToArray());
        return rows;
    }
}

/// <summary>
/// Desktop multi-selection over stable tile keys. Selection never depends on visual-container identity,
/// so it survives recycling and non-destructive refresh.
/// </summary>
internal sealed class BrowserGridSelection
{
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    public int? AnchorIndex { get; private set; }
    public bool IsSelected(string key) => _selected.Contains(key);
    public IReadOnlySet<string> Snapshot() => new HashSet<string>(_selected, StringComparer.Ordinal);

    public void SelectSingle(string key, int index)
    {
        _selected.Clear();
        _selected.Add(key);
        AnchorIndex = index;
    }

    public void ToggleCtrl(string key, int index)
    {
        if (!_selected.Remove(key)) _selected.Add(key);
        AnchorIndex = index;
    }

    /// <summary>Replaces the selection with the given keys without moving the shift-range anchor.</summary>
    public void SelectRange(IReadOnlyList<string> keys)
    {
        _selected.Clear();
        foreach (var key in keys) _selected.Add(key);
    }

    public void SelectAll(IEnumerable<string> keys)
    {
        _selected.Clear();
        foreach (var key in keys) _selected.Add(key);
    }

    public void Clear()
    {
        _selected.Clear();
        AnchorIndex = null;
    }

    /// <summary>Drops selected keys that no longer exist, e.g. after a non-destructive refresh.</summary>
    public void Retain(IEnumerable<string> stillPresentKeys) => _selected.IntersectWith(stillPresentKeys);
}

/// <summary>
/// Owns the Browser thumbnail grid's tiles, row-based virtualization grouping, and selection state.
/// Consumes only the existing #98-#100 discovery/reconciliation/derived-work outputs; it never enumerates
/// files, resolves Catalog identity, or generates thumbnails itself.
/// </summary>
internal sealed class BrowserGridModel
{
    private List<BrowserGridTile> _tiles = [];
    private readonly Dictionary<Guid, BrowserGridTile> _tilesByAsset = [];
    private readonly BrowserGridSelection _selection = new();
    private int _columns = 1;

    public ObservableCollection<BrowserGridRow> Rows { get; } = [];
    public IReadOnlyList<BrowserGridTile> Tiles => _tiles;
    public IReadOnlySet<string> SelectedKeys => _selection.Snapshot();

    /// <summary>
    /// Lightflow's Browser is a media browser, not a general-purpose file browser: the central canvas
    /// presents only supported still image, RAW image, and video assets. This reads the classification
    /// the existing #98 media type registry already assigned to the entry; it does not maintain a second,
    /// WPF-owned extension list. Folders, standalone audio, and unknown/unsupported files are excluded
    /// from presentation entirely rather than shown as a placeholder tile.
    /// </summary>
    public static bool IsPresentable(MediaFolderEntry entry) => !entry.IsDirectory && entry.MediaType.Category
        is MediaTypeCategory.StillImage or MediaTypeCategory.RawImage or MediaTypeCategory.Video;

    /// <summary>
    /// Replaces the tile set from the current folder's file entries in deterministic enumeration order,
    /// reusing existing tile instances by stable key so already-resolved thumbnails and selection survive
    /// a non-destructive refresh.
    /// </summary>
    public void Populate(IReadOnlyList<MediaFolderEntry> entries)
    {
        var existingByKey = _tiles.ToDictionary(tile => tile.Key, StringComparer.Ordinal);
        var desired = new List<BrowserGridTile>(entries.Count);
        var index = 0;
        foreach (var entry in entries)
        {
            if (!IsPresentable(entry)) continue;
            if (existingByKey.TryGetValue(entry.RelativePathKey, out var prior))
            {
                prior.Index = index;
                desired.Add(prior);
            }
            else desired.Add(new BrowserGridTile(entry, index));
            index++;
        }

        _tiles = desired;
        _tilesByAsset.Clear();
        foreach (var tile in _tiles.Where(tile => tile.AssetId is not null))
            _tilesByAsset[tile.AssetId!.Value] = tile;

        _selection.Retain(_tiles.Select(tile => tile.Key));
        var selected = _selection.Snapshot();
        foreach (var tile in _tiles) tile.IsSelected = selected.Contains(tile.Key);

        Rebuild();
    }

    /// <summary>Maps freshly-reconciled Catalog identity onto tiles so later thumbnail results can be applied.</summary>
    public void ApplyAssetIdentities(IReadOnlyList<CatalogReconciliationItem> items)
    {
        if (_tiles.Count == 0 || items.Count == 0) return;
        var byKey = _tiles.ToDictionary(tile => tile.Key, StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = MediaPathSemantics.RelativePathKey(item.RelativePath);
            if (!byKey.TryGetValue(key, out var tile)) continue;
            tile.SetAssetId(item.AssetId);
            _tilesByAsset[item.AssetId] = tile;
        }
    }

    public bool HasThumbnail(Guid assetId) => _tilesByAsset.TryGetValue(assetId, out var tile) && tile.HasThumbnail;

    /// <summary>Updates one tile's thumbnail in place. Never rebuilds rows or touches unrelated tiles.</summary>
    public void ApplyThumbnail(Guid assetId, string absoluteThumbnailPath)
    {
        if (_tilesByAsset.TryGetValue(assetId, out var tile)) tile.ThumbnailPath = absoluteThumbnailPath;
    }

    public void SetColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (columns == _columns) return;
        _columns = columns;
        Rebuild();
    }

    public void SelectSingle(int index) => ApplySelectionChange(() =>
    {
        if (index < 0 || index >= _tiles.Count) return;
        _selection.SelectSingle(_tiles[index].Key, index);
    });

    public void ToggleCtrl(int index) => ApplySelectionChange(() =>
    {
        if (index < 0 || index >= _tiles.Count) return;
        _selection.ToggleCtrl(_tiles[index].Key, index);
    });

    public void SelectRange(int index) => ApplySelectionChange(() =>
    {
        if (index < 0 || index >= _tiles.Count) return;
        var anchor = _selection.AnchorIndex ?? index;
        var low = Math.Min(anchor, index);
        var high = Math.Max(anchor, index);
        var keys = _tiles.Skip(low).Take(high - low + 1).Select(tile => tile.Key).ToArray();
        _selection.SelectRange(keys);
    });

    public void SelectAll() => ApplySelectionChange(() => _selection.SelectAll(_tiles.Select(tile => tile.Key)));

    public void ClearSelection() => ApplySelectionChange(_selection.Clear);

    private void ApplySelectionChange(Action mutate)
    {
        var before = _selection.Snapshot();
        mutate();
        var after = _selection.Snapshot();
        if (before.SetEquals(after)) return;
        foreach (var tile in _tiles)
        {
            var selected = after.Contains(tile.Key);
            if (tile.IsSelected != selected) tile.IsSelected = selected;
        }
    }

    private void Rebuild()
    {
        var grouped = BrowserGridLayout.BuildRows(_tiles, _columns);
        for (var index = 0; index < grouped.Count; index++)
        {
            if (index < Rows.Count) Rows[index].SetTiles(grouped[index]);
            else Rows.Add(new BrowserGridRow(grouped[index]));
        }
        while (Rows.Count > grouped.Count) Rows.RemoveAt(Rows.Count - 1);
    }
}

/// <summary>
/// Pure projection over derived-work results, used to decide which completed assets still need their
/// generated thumbnail path applied to the grid. Kept separate from Dispatcher/UI glue for testability.
/// </summary>
internal static class BrowserDerivedWorkProjection
{
    /// <summary>
    /// A thumbnail is worth looking up whenever the scheduler reports the thumbnail component as
    /// <see cref="DerivedWorkComponentOutcome.Succeeded"/> or <see cref="DerivedWorkComponentOutcome.Current"/>
    /// (freshly generated or freshly verified this batch), or <see cref="DerivedWorkComponentOutcome.NotNeeded"/>
    /// — which is what an asset reports when its thumbnail was already current *before* this batch started,
    /// so the scheduler skipped calling the generator at all. That is the common case for any previously-viewed
    /// folder and must still resolve the already-existing cached thumbnail, not just newly-generated ones.
    /// <see cref="DerivedWorkComponentOutcome.Failed"/>, <see cref="DerivedWorkComponentOutcome.SkippedUnavailable"/>,
    /// and <see cref="DerivedWorkComponentOutcome.Canceled"/> have no thumbnail to fetch.
    /// </summary>
    public static IReadOnlyList<Guid> AssetsNeedingThumbnailLookup(
        IReadOnlyList<DerivedWorkItemResult> results, Func<Guid, bool> alreadyHasThumbnail) =>
        results.Where(result => result.Thumbnail is DerivedWorkComponentOutcome.Succeeded or
            DerivedWorkComponentOutcome.Current or DerivedWorkComponentOutcome.NotNeeded)
            .Select(result => result.AssetId)
            .Where(assetId => !alreadyHasThumbnail(assetId))
            .Distinct()
            .ToArray();
}
