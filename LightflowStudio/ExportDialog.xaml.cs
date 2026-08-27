using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace LightflowStudio;

public partial class ExportDialog : Window
{
    private readonly ExportDialogModel _model;
    private readonly ExportJobCoordinator _coordinator;
    private readonly string? _ffprobe;
    private bool _initializing = true;
    private System.Windows.Point _dragStart;
    private int? _dragPartIndex;

    internal ExportDialog(ExportDialogModel model, ExportJobCoordinator coordinator, string? ffprobe)
    {
        _model = model; _coordinator = coordinator; _ffprobe = ffprobe;
        InitializeComponent();
        Title = ExportHeading.Text = model.Title;
        FilesToExportHeading.Text = $"Files to Export · {model.Inputs.Count}";
        System.Windows.Automation.AutomationProperties.SetName(FilesToExportScroll, model.FilesAutomationName);
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
        DestinationText.Text = model.Destination;
        CreateSubfolderCheck.IsChecked = model.CreateSubfolder;
        AddPartCombo.ItemsSource = ExportPresentation.NameParts; AddPartCombo.SelectedIndex = 0;
        SeparatorCombo.ItemsSource = ExportPresentation.Separators; Select(SeparatorCombo, ExportPresentation.Separators, model.Separator);
        ContainerCombo.ItemsSource = ExportPresentation.Containers; Select(ContainerCombo, ExportPresentation.Containers, model.Container);
        CodecCombo.ItemsSource = ExportPresentation.Codecs; Select(CodecCombo, ExportPresentation.Codecs, model.Codec);
        RateControlCombo.ItemsSource = ExportPresentation.RateControls; Select(RateControlCombo, ExportPresentation.RateControls, model.Encoding.RateControl);
        ResolutionCombo.ItemsSource = ExportPresentation.Resolutions; Select(ResolutionCombo, ExportPresentation.Resolutions, model.Resolution);
        FrameRateCombo.ItemsSource = new[] { "Same as Source", "23.976", "24", "25", "29.97", "30", "50", "59.94", "60" }; FrameRateCombo.SelectedIndex = 0;
        AudioCombo.ItemsSource = new[] { "Use source audio", "AAC", "No audio" }; AudioCombo.SelectedIndex = (int)model.Encoding.AudioMode;
        CameraCombo.ItemsSource = model.CameraChoices; CameraCombo.SelectedIndex = 0;
        CreativeCombo.ItemsSource = model.CreativeChoices; CreativeCombo.SelectedIndex = 0;
        OverwriteExistingCheck.IsChecked = model.OverwriteExisting;
        TuneCombo.ItemsSource = ExportPresentation.Tunes; Select(TuneCombo, ExportPresentation.Tunes, model.Encoding.Tune);
        MultipassCombo.ItemsSource = ExportPresentation.MultipassModes; Select(MultipassCombo, ExportPresentation.MultipassModes, model.Encoding.Multipass);
        PixelFormatCombo.ItemsSource = ExportPresentation.PixelFormats; Select(PixelFormatCombo, ExportPresentation.PixelFormats, model.Encoding.PixelFormat);
        PresetCombo.ItemsSource = ExportPresentation.EncoderPresets; Select(PresetCombo, ExportPresentation.EncoderPresets, model.Encoding.EncoderPreset);
        QualitySlider.Value = ExportPresentation.ConstantQuality(model.Encoding.Quality); TargetText.Text = model.Encoding.TargetBitrateMbps.ToString(); MaxText.Text = model.Encoding.MaxBitrateMbps.ToString(); CbrText.Text = model.Encoding.TargetBitrateMbps.ToString(); AqStrengthSlider.Value = ExportPresentation.AqStrength(model.Encoding.AqStrength);
        SpatialAqCheck.IsChecked = model.Encoding.SpatialAq; TemporalAqCheck.IsChecked = model.Encoding.TemporalAq; DeinterlaceCheck.IsChecked = model.Encoding.Deinterlace; FastStartCheck.IsChecked = model.Encoding.FastStart;
        _initializing = false; Sync(); Loaded += async (_, _) =>
        {
            ConstrainToCurrentWorkArea();
            try { await InitializePreflightAsync(); }
            catch (Exception exception)
            {
                _model.ApplyEncoderCapability(new(EncoderBackend.NvidiaNvenc,
                    EncoderCapabilityState.ImplementedButUnavailable, exception.Message));
                QueueError.Text = $"Preflight could not complete: {exception.Message}";
                Sync();
            }
        };
    }

    private void ConstrainToCurrentWorkArea()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        MaxHeight = Math.Max(480, area.Height / dpi.DpiScaleY - 32);
        MaxWidth = Math.Max(760, area.Width / dpi.DpiScaleX - 32);
        MinHeight = Math.Min(620, MaxHeight);
        MinWidth = Math.Min(940, MaxWidth);
        Height = Math.Min(900, MaxHeight);
        Width = Math.Min(1120, MaxWidth);
    }

    private async Task InitializePreflightAsync()
    {
        var capabilities = await new EncoderCapabilityService(_ffprobe is null ? null : Path.Combine(Path.GetDirectoryName(_ffprobe)!, "ffmpeg.exe"), new FfmpegEncoderCapabilityProbe()).GetAsync();
        var nvenc = capabilities.Single(x => x.Backend == EncoderBackend.NvidiaNvenc);
        EncoderCombo.ItemsSource = ExportPresentation.Encoders; EncoderCombo.SelectedIndex = 0;
        var hardware = ExportPresentation.Hardware(nvenc);
        HardwareStatusHeading.Text = hardware.Heading;
        HardwareStatusHeading.Foreground = (System.Windows.Media.Brush)FindResource(hardware.Available ? "SuccessBrush" : "WarningBrush");
        EncoderDiagnostic.Text = hardware.Detail;
        EncoderDiagnostic.ToolTip = hardware.Diagnostic;
        _model.ApplyEncoderCapability(nvenc);
        var metadata = new List<MediaMetadata?>();
        foreach (var input in _model.Inputs) metadata.Add(await ProbeAsync(input.SourcePath));
        _model.ApplyMetadata(metadata);
        var ranges = new List<ResolvedMediaRange?>();
        for (var index = 0; index < _model.Inputs.Count; index++)
            ranges.Add(await ResolveRangeAsync(_model.Inputs[index], metadata[index]));
        _model.ApplyResolvedRanges(ranges); Sync();
    }

    private async Task<MediaMetadata?> ProbeAsync(string path)
    {
        if (_ffprobe is null) return null;
        try
        {
            var start = new ProcessStartInfo(_ffprobe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in FfmpegCommandBuilder.ProbeMetadata(path)) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; var output = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync();
            return process.ExitCode == 0 && MediaMetadataParser.TryParse(output, new FileInfo(path).Length, out var value) ? value : null;
        }
        catch { return null; }
    }

    private async Task<ResolvedMediaRange?> ResolveRangeAsync(EncodingHandoffInput input, MediaMetadata? metadata)
    {
        if (input.InitialTrim is not { IsFullSource: false } range || _ffprobe is null) return null;
        var identity = TrimSourceIdentity.Read(input.SourcePath);
        if (identity is null || identity.FileSizeBytes != input.FileSizeBytes) return null;
        var startTimestamp = metadata?.StartTimestamp ?? TimeSpan.Zero;
        var packets = await CaptureProbeAsync(FfmpegCommandBuilder.ProbeVideoPackets(input.SourcePath, range, startTimestamp));
        try { return EncodingRangeResolver.Resolve(range, startTimestamp, packets); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            var frames = await CaptureProbeAsync(FfmpegCommandBuilder.ProbeVideoFrames(input.SourcePath, range, startTimestamp));
            return EncodingRangeResolver.Resolve(range, startTimestamp, frames);
        }
    }

    private async Task<string> CaptureProbeAsync(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(_ffprobe!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start FFprobe.");
        var output = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidDataException("FFprobe could not validate the saved range.");
        return output;
    }

    private void Configuration_Changed(object sender, RoutedEventArgs e) { if (!_initializing) ReadControls(); }
    private void Configuration_Changed(object sender, TextChangedEventArgs e) { if (!_initializing) ReadControls(); }
    private void Configuration_Changed(object sender, SelectionChangedEventArgs e) { if (!_initializing) ReadControls(); }
    private void ReadControls()
    {
        _model.Destination = DestinationText.Text; _model.CreateSubfolder = CreateSubfolderCheck.IsChecked == true; _model.SubfolderName = SubfolderText.Text;
        if (SeparatorCombo.SelectedItem is ExportChoice<NamePartSeparator> separator) _model.Separator = separator.Value;
        if (ContainerCombo.SelectedItem is ExportChoice<ExportContainerChoice> container) _model.Container = container.Value;
        if (CodecCombo.SelectedItem is ExportChoice<ExportCodecChoice> codec) _model.Codec = codec.Value;
        if (ResolutionCombo.SelectedItem is ExportChoice<OutputResolution> resolution) _model.Resolution = resolution.Value;
        _model.OverwriteExisting = OverwriteExistingCheck.IsChecked == true; _model.Camera = CameraCombo.SelectedItem as ExportLutChoice ?? _model.Camera; _model.Creative = CreativeCombo.SelectedItem as ExportLutChoice ?? _model.Creative;
        var frameRates = new[] { 0d, 23.976, 24, 25, 29.97, 30, 50, 59.94, 60 };
        var encoding = _model.Encoding with { RateControl = (RateControlCombo.SelectedItem as ExportChoice<RateControlMode>)?.Value ?? _model.Encoding.RateControl, FrameRate = frameRates[Math.Max(0, FrameRateCombo.SelectedIndex)], AudioMode = (AudioEncodingMode)Math.Max(0, AudioCombo.SelectedIndex), Tune = (TuneCombo.SelectedItem as ExportChoice<EncoderTune>)?.Value ?? _model.Encoding.Tune, Multipass = (MultipassCombo.SelectedItem as ExportChoice<MultipassMode>)?.Value ?? _model.Encoding.Multipass, PixelFormat = (PixelFormatCombo.SelectedItem as ExportChoice<VideoPixelFormat>)?.Value ?? _model.Encoding.PixelFormat, SpatialAq = SpatialAqCheck.IsChecked == true, TemporalAq = TemporalAqCheck.IsChecked == true, Deinterlace = DeinterlaceCheck.IsChecked == true, FastStart = FastStartCheck.IsChecked == true };
        if (encoding.RateControl == RateControlMode.ConstantQuality) encoding = encoding with { Quality = ExportPresentation.ConstantQuality((int)Math.Round(QualitySlider.Value)) };
        if (encoding.RateControl == RateControlMode.ConstantBitrate && int.TryParse(CbrText.Text, out var cbr)) encoding = encoding with { TargetBitrateMbps = cbr };
        else if (int.TryParse(TargetText.Text, out var target)) encoding = encoding with { TargetBitrateMbps = target };
        if (int.TryParse(MaxText.Text, out var max)) encoding = encoding with { MaxBitrateMbps = max };
        if (PresetCombo.SelectedItem is ExportChoice<int> preset) encoding = encoding with { EncoderPreset = preset.Value };
        encoding = encoding with { AqStrength = ExportPresentation.AqStrength((int)Math.Round(AqStrengthSlider.Value)) };
        _model.Encoding = encoding; _model.AdvancedExpanded = AdvancedExpander.IsExpanded; Sync();
    }
    private void Sync()
    {
        var wasInitializing = _initializing;
        _initializing = true;
        FilesToExportItems.ItemsSource = _model.SubmissionItems;
        GlobalUseRangesCheck.IsChecked = _model.GlobalUseRangeState;
        _initializing = wasInitializing;
        NamePartsComposer.ItemsSource = ExportPresentation.Composer(_model.NameParts);
        NamePreview.Text = _model.PreviewName; PathPreview.Text = _model.PreviewPath;
        PathPreview.ToolTip = OutputExampleBorder.ToolTip = _model.PreviewPath;
        ExtensionPreview.Text = _model.RepresentativeExtension + (_model.HasHeterogeneousExtensions ? "  (varies)" : "");
        ExtensionPreview.ToolTip = _model.ExtensionHelp;
        var cq = _model.Encoding.RateControl == RateControlMode.ConstantQuality;
        var vbr = _model.Encoding.RateControl == RateControlMode.VariableBitrate;
        var cbr = _model.Encoding.RateControl == RateControlMode.ConstantBitrate;
        QualityPrimaryLabel.Visibility = cq ? Visibility.Visible : Visibility.Collapsed;
        TargetPrimaryLabel.Visibility = TargetText.Visibility = vbr ? Visibility.Visible : Visibility.Collapsed;
        MaxPrimaryLabel.Visibility = MaxText.Visibility = vbr ? Visibility.Visible : Visibility.Collapsed;
        CbrPrimaryLabel.Visibility = CbrText.Visibility = cbr ? Visibility.Visible : Visibility.Collapsed;
        AqSettingLayout.IsEnabled = ExportPresentation.IsAqStrengthEnabled(SpatialAqCheck.IsChecked == true, TemporalAqCheck.IsChecked == true);
        if (cbr && CbrText.Text != _model.Encoding.TargetBitrateMbps.ToString())
        { _initializing = true; CbrText.Text = _model.Encoding.TargetBitrateMbps.ToString(); _initializing = false; }
        System.Windows.Automation.AutomationProperties.SetHelpText(AdvancedExpander,
            AdvancedExpander.IsExpanded ? "Advanced export settings expanded" : "Advanced export settings collapsed");
        var lines = _model.IsAnalyzing ? [] : _model.GlobalErrors.Select(x => "Error — " + x.Message).Concat(_model.GlobalWarnings.Select(x => "Warning — " + x.Message)).ToList();
        ValidationText.Text = string.Join(Environment.NewLine, lines);
        ValidationBorder.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReadySummaryText.Text = _model.ReadySummary;
        ExportButton.IsEnabled = _model.CanExport;
    }
    private void RangeUse_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing || sender is not System.Windows.Controls.CheckBox { DataContext: ExportSubmissionItem item } check) return;
        _model.SetUseRange(item.Index, check.IsChecked == true);
        Sync();
    }
    private void GlobalUseRanges_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing || GlobalUseRangesCheck.IsChecked is not { } use) return;
        _model.SetGlobalUseRanges(use);
        Sync();
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "Select the output folder", UseDescriptionForTitle = true };
        if (MainWindow.ResolveFolderPickerInitialDirectory(DestinationText.Text) is { } start)
        {
            dialog.InitialDirectory = start;
            dialog.SelectedPath = start;
        }
        if (dialog.ShowDialog() == Forms.DialogResult.OK) DestinationText.Text = dialog.SelectedPath;
    }
    private void AddPart_Click(object sender, RoutedEventArgs e) { if (AddPartCombo.SelectedItem is ExportChoice<NamePartKind> choice) _model.AddPart(choice.Value); Sync(); }
    private void RemovePart_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is ExportNamePartChip chip) { _model.RemovePart(chip.Index); Sync(); } }
    private void MovePartEarlier_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is ExportNamePartChip chip) { _model.MovePart(chip.Index, -1); Sync(); } }
    private void MovePartLater_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is ExportNamePartChip chip) { _model.MovePart(chip.Index, 1); Sync(); } }
    private void CustomPartText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_initializing || sender is not System.Windows.Controls.TextBox { DataContext: ExportNamePartChip chip } box || box.Text == chip.Part.Text) return;
        _model.UpdateCustomText(chip.Index, box.Text); NamePreview.Text = _model.PreviewName; PathPreview.Text = _model.PreviewPath;
    }

    private void InfoButton_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button) return;
        if (button.ToolTip is string help)
            button.ToolTip = new System.Windows.Controls.ToolTip { Content = help, PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };
        if (button.ToolTip is System.Windows.Controls.ToolTip toolTip) toolTip.IsOpen = true;
    }

    private void InfoButton_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ToolTip: System.Windows.Controls.ToolTip toolTip }) toolTip.IsOpen = false;
    }
    private void RangeTimeline_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.ToolTip is string help)
            element.ToolTip = new System.Windows.Controls.ToolTip
            {
                Content = help,
                PlacementTarget = element,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };
        if (element.ToolTip is System.Windows.Controls.ToolTip toolTip) toolTip.IsOpen = true;
    }
    private void RangeTimeline_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { ToolTip: System.Windows.Controls.ToolTip toolTip }) toolTip.IsOpen = false;
    }
    private void NamePart_MouseDown(object sender, MouseButtonEventArgs e)
    { if (((FrameworkElement)sender).DataContext is ExportNamePartChip chip) { _dragStart = e.GetPosition(this); _dragPartIndex = chip.Index; } }
    private void NamePart_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragPartIndex is null) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, _dragPartIndex.Value, System.Windows.DragDropEffects.Move);
    }
    private void NamePart_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(int)) || ((FrameworkElement)sender).DataContext is not ExportNamePartChip target) return;
        var source = (int)e.Data.GetData(typeof(int));
        if (source != target.Index) _model.MovePart(source, target.Index - source);
        _dragPartIndex = null; Sync();
    }
    private static void Select<T>(System.Windows.Controls.ComboBox combo, IReadOnlyList<ExportChoice<T>> choices, T value) => combo.SelectedItem = choices.First(x => EqualityComparer<T>.Default.Equals(x.Value, value));
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        ExportButton.IsEnabled=false; QueueError.Text="";
        try { var plan=await _model.MaterializeAcceptedPlanAsync(); if(!plan.IsValid) { Sync(); return; } _coordinator.Queue(plan); DialogResult=true; }
        catch(Exception ex) { QueueError.Text=ex.Message; Sync(); }
    }
}
