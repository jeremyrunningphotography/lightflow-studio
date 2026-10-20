namespace LightflowStudio;

public sealed class BrowserGridSelection
{
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    public BrowserGridSelection Copy()
    {
        var copy = new BrowserGridSelection { AnchorIndex = AnchorIndex };
        copy._selected.UnionWith(_selected);
        return copy;
    }
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

    /// <summary>
    /// Clears the shift-range anchor without touching the selection itself. A changed sort/filter/search
    /// reorders and re-numbers the visible set, so an old anchor index would produce a wrong range on the
    /// next Shift+click; the selected items themselves remain exactly as they were.
    /// </summary>
    public void ResetAnchor() => AnchorIndex = null;

    public void Restore(IEnumerable<string> keys, int? anchor)
    {
        _selected.Clear();
        _selected.UnionWith(keys);
        AnchorIndex = anchor;
    }
}

