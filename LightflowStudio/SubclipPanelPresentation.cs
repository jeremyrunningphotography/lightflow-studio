using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace LightflowStudio;

internal sealed class SubclipPanelItem(Subclip subclip) : INotifyPropertyChanged
{
    private Subclip _subclip = subclip;
    private ImageSource? _poster;
    private bool _isSelected;
    private bool _isEditing;

    public Subclip Subclip => _subclip;
    public Guid SubclipId => _subclip.SubclipId;
    public string Name => _subclip.Name;
    public string RangeSummary => $"{Format(_subclip.In)} – {Format(_subclip.Out)}";
    public string DurationSummary => $"{Format(_subclip.Out - _subclip.In)} duration";
    public ImageSource? Poster { get => _poster; set => Set(ref _poster, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }
    public bool CanMoveUp { get; private set; }
    public bool CanMoveDown { get; private set; }

    public void SetMoveBoundaries(bool canMoveUp, bool canMoveDown)
    {
        if (CanMoveUp != canMoveUp) { CanMoveUp = canMoveUp; Changed(nameof(CanMoveUp)); }
        if (CanMoveDown != canMoveDown) { CanMoveDown = canMoveDown; Changed(nameof(CanMoveDown)); }
    }

    public void Replace(Subclip subclip)
    {
        _subclip = subclip;
        Changed(nameof(Subclip)); Changed(nameof(Name)); Changed(nameof(RangeSummary)); Changed(nameof(DurationSummary));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed(name);
    }
    private void Changed(string? name) => PropertyChanged?.Invoke(this, new(name));
    private static string Format(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
}
