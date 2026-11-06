using System.Collections;
using LightflowStudio;

namespace Lightflow.UI;

public enum BrowserCardMode { Preview, Info, Hybrid }

/// <summary>Windows Medium geometry (BrowserGridLayout/MainWindow); no discovery or Preview work.</summary>
public static class BrowserCardLayout
{
    public const double Width = 168, Gap = 12, PreviewHeight = 108, InfoPreviewHeight = 78;
    public static int Columns(double width) => double.IsFinite(width) ? Math.Max(1, (int)(width / (Width + Gap))) : 1;
    public static double Height(BrowserCardMode mode) => mode switch
    {
        BrowserCardMode.Preview => 124,
        BrowserCardMode.Info => 161,
        BrowserCardMode.Hybrid => 145,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    public static int Move(int current, int count, int columns, Avalonia.Input.Key key, int pageRows = 1) =>
        count == 0 ? -1 : Math.Clamp(key switch
        {
            Avalonia.Input.Key.Left => current - 1,
            Avalonia.Input.Key.Right => current + 1,
            Avalonia.Input.Key.Up => current - columns,
            Avalonia.Input.Key.Down => current + columns,
            Avalonia.Input.Key.Home => 0,
            Avalonia.Input.Key.End => count - 1,
            Avalonia.Input.Key.PageUp => current - columns * pageRows,
            Avalonia.Input.Key.PageDown => current + columns * pageRows,
            _ => current
        }, 0, count - 1);
}

internal sealed record BrowserCardRow(IReadOnlyList<MediaAsset> Assets, int Start, int Count);

/// <summary>O(1) row projection over the existing snapshot, even on resize of a large folder.</summary>
internal sealed class BrowserCardRows(IReadOnlyList<MediaAsset> assets, int columns) : IList
{
    public int Count => (assets.Count + columns - 1) / columns;
    public object? this[int index] { get => new BrowserCardRow(assets, index * columns, Math.Min(columns, assets.Count - index * columns)); set => throw new NotSupportedException(); }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int IndexOf(object? value) => value is BrowserCardRow row && ReferenceEquals(row.Assets, assets) ? row.Start / columns : -1;
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public IEnumerator GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
    public void CopyTo(Array array, int index) { foreach (var row in this) array.SetValue(row, index++); }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
