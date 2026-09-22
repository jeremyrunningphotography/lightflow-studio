using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

/// <summary>
/// Displays a source-oriented cached frame with the Catalog adjustment. Cached pixels deliberately exclude
/// authored rotation: no regeneration is needed, including offline, and Color/frame cache identity is unchanged.
/// This control must not wrap native Player snapshots, whose pixels already include the adjustment.
/// </summary>
public sealed class OrientedPreviewImage : System.Windows.Controls.Image
{
    internal static readonly DependencyProperty StoreProperty = DependencyProperty.RegisterAttached(
        "Store", typeof(IAssetVideoRotationStore), typeof(OrientedPreviewImage),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, StoreChanged));
    internal static void SetStore(DependencyObject owner, IAssetVideoRotationStore value) => owner.SetValue(StoreProperty, value);
    public static readonly DependencyProperty AssetIdProperty = DependencyProperty.Register(nameof(AssetId),
        typeof(Guid?), typeof(OrientedPreviewImage), new PropertyMetadata(null, AssetChanged));
    public Guid? AssetId { get => (Guid?)GetValue(AssetIdProperty); set => SetValue(AssetIdProperty, value); }
    private IAssetVideoRotationStore? _store;
    private long _generation;
    private long _revision = -1;
    private VideoRotation _rotation;
    private bool _orientationUnavailable;

    static OrientedPreviewImage() => SourceProperty.OverrideMetadata(typeof(OrientedPreviewImage),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceSource));

    public OrientedPreviewImage()
    {
        Loaded += (_, _) => Connect();
        Unloaded += (_, _) => Disconnect();
    }

    private static object? CoerceSource(DependencyObject owner, object? value)
    {
        var image = (OrientedPreviewImage)owner;
        if (image._orientationUnavailable) return null;
        if (value is not BitmapSource bitmap || image._rotation.Degrees == 0) return value;
        var rotated = new TransformedBitmap(bitmap, new RotateTransform(image._rotation.Degrees));
        if (rotated.CanFreeze) rotated.Freeze();
        return rotated;
    }
    private static void StoreChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    { if (owner is OrientedPreviewImage image && image.IsLoaded) image.Connect(); }
    private static void AssetChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var image = (OrientedPreviewImage)owner;
        image._revision = -1;
        image._rotation = default;
        image._orientationUnavailable = false;
        image.CoerceValue(SourceProperty);
        if (image.IsLoaded) image.Connect();
    }
    private void Disconnect()
    {
        ++_generation;
        if (_store is not null) { _store.Changed -= RotationChanged; _store.Invalidated -= CatalogInvalidated; }
        _store = null;
    }
    private async void Connect()
    {
        Disconnect();
        _revision = -1;
        _store = GetValue(StoreProperty) as IAssetVideoRotationStore;
        if (_store is null || AssetId is not Guid id) return;
        _store.Changed += RotationChanged;
        _store.Invalidated += CatalogInvalidated;
        var generation = _generation;
        try
        {
            var values = await _store.GetAsync([id]);
            if (generation == _generation)
            {
                if (values.TryGetValue(id, out var value)) Apply(value);
                else { _rotation = default; _orientationUnavailable = false; CoerceValue(SourceProperty); }
            }
        }
        catch (Exception error)
        {
            if (generation == _generation && _revision < 0)
            {
                // Do not present an unverified orientation as the user's chosen orientation.
                _orientationUnavailable = true;
                CoerceValue(SourceProperty);
                ToolTip = error.Message;
            }
        }
    }
    private void RotationChanged(object? sender, IReadOnlyList<AssetVideoRotation> values)
    {
        var generation = _generation;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _generation) return;
            foreach (var value in values) if (value.AssetId == AssetId) Apply(value);
        });
    }
    private void CatalogInvalidated(object? sender, EventArgs args)
    {
        var generation = _generation;
        Dispatcher.BeginInvoke(() => { if (generation == _generation && IsLoaded) Connect(); });
    }
    private void Apply(AssetVideoRotation value)
    {
        if (value.Revision < _revision) return;
        _revision = value.Revision;
        _orientationUnavailable = false;
        _rotation = value.Rotation;
        CoerceValue(SourceProperty);
    }
}
