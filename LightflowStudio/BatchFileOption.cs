using System.ComponentModel;
using System.IO;

namespace LightflowStudio;

internal sealed class BatchFileOption : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _detailsText;
    private string _detailsTooltip = "";
    private string _warningLabel = "";
    private string _warningTooltip = "";
    private string _resolutionText = "Reading…";
    private string _frameRateText = "—";
    private string _durationText = "—";
    private MediaRange? _trimRange;
    private TrimIndicatorPresentation _trimIndicator = TrimIndicatorPresentation.For(null, null);

    public BatchFileOption(string filePath, string displayName, long fileSizeBytes = 0)
    {
        FilePath = filePath;
        DisplayName = displayName;
        FileSizeBytes = fileSizeBytes;
        SourceIdentity = TrimSourceIdentity.Read(filePath);
        _detailsText = $"Reading details… · {MediaMetadataPresentation.FormatSize(fileSizeBytes)}";
    }

    public string FilePath { get; }
    public string DisplayName { get; }
    public long FileSizeBytes { get; }
    public TrimSourceIdentity? SourceIdentity { get; }
    public MediaMetadata? Metadata { get; private set; }
    public bool MetadataError { get; private set; }
    public bool IsAnalyzing => Metadata is null && !MetadataError;
    public string DetailsText { get => _detailsText; private set => SetField(ref _detailsText, value); }
    public string DetailsTooltip { get => _detailsTooltip; private set => SetField(ref _detailsTooltip, value); }
    public string WarningLabel { get => _warningLabel; private set => SetField(ref _warningLabel, value); }
    public string WarningTooltip { get => _warningTooltip; private set => SetField(ref _warningTooltip, value); }
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningLabel);
    public string ResolutionText { get => _resolutionText; private set => SetField(ref _resolutionText, value); }
    public string FrameRateText { get => _frameRateText; private set => SetField(ref _frameRateText, value); }
    public string DurationText { get => _durationText; private set => SetField(ref _durationText, value); }
    public string SizeText => MediaMetadataPresentation.FormatSize(FileSizeBytes);
    public MediaRange? TrimRange => _trimRange;
    public bool HasActiveTrim => _trimRange is not null;
    public string TrimActionText => HasActiveTrim ? "Edit Trim" : "Trim";
    public bool CanTrim => SourceIdentity is not null;
    public bool TrimIndicatorHasProportions => _trimIndicator.HasProportions;
    public double TrimIndicatorStart => _trimIndicator.StartFraction;
    public double TrimIndicatorWidth => _trimIndicator.WidthFraction;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public void ApplyMetadata(MediaMetadata metadata)
    {
        Metadata = metadata;
        MetadataError = false;
        DetailsText = MediaMetadataPresentation.Details(metadata);
        DetailsTooltip = MediaMetadataPresentation.Tooltip(metadata);
        ResolutionText = $"{metadata.Width}×{metadata.Height}";
        FrameRateText = $"{MediaMetadataPresentation.FormatFrameRate(metadata.FrameRate)} fps";
        DurationText = MediaMetadataPresentation.FormatDuration(metadata.DurationSeconds);
        OnPropertyChanged(nameof(IsAnalyzing));
        OnPropertyChanged(nameof(CanTrim));
        UpdateTrimIndicator();
    }

    public void MarkMetadataUnavailable()
    {
        Metadata = null;
        MetadataError = true;
        DetailsText = $"Details unavailable · {MediaMetadataPresentation.FormatSize(FileSizeBytes)}";
        DetailsTooltip = "FFprobe could not read media details.";
        ResolutionText = "Unavailable";
        FrameRateText = "—";
        DurationText = "—";
        OnPropertyChanged(nameof(IsAnalyzing));
        OnPropertyChanged(nameof(CanTrim));
        UpdateTrimIndicator();
    }

    public void SetWarning(string label, string tooltip)
    {
        WarningLabel = label;
        WarningTooltip = tooltip;
    }

    public void ApplyTrim(MediaRange? range)
    {
        if (range is not null && (range.IsFullSource || range.Validate().Count != 0))
            throw new ArgumentException("The applied trim must be a valid partial media range.", nameof(range));
        _trimRange = range;
        OnPropertyChanged(nameof(TrimRange));
        OnPropertyChanged(nameof(HasActiveTrim));
        OnPropertyChanged(nameof(TrimActionText));
        UpdateTrimIndicator();
    }

    private void UpdateTrimIndicator()
    {
        TimeSpan? duration = Metadata?.DurationSeconds > 0 ? TimeSpan.FromSeconds(Metadata.DurationSeconds) : null;
        _trimIndicator = TrimIndicatorPresentation.For(_trimRange, duration);
        OnPropertyChanged(nameof(TrimIndicatorHasProportions));
        OnPropertyChanged(nameof(TrimIndicatorStart));
        OnPropertyChanged(nameof(TrimIndicatorWidth));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
