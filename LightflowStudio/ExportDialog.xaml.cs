using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace LightflowStudio;

public partial class ExportDialog : Window
{
    private readonly ExportDialogModel _model;
    private readonly ExportJobCoordinator _coordinator;
    private readonly string? _ffprobe;
    private bool _initializing = true;

    internal ExportDialog(ExportDialogModel model, ExportJobCoordinator coordinator, string? ffprobe)
    {
        _model = model; _coordinator = coordinator; _ffprobe = ffprobe;
        InitializeComponent();
        DestinationText.Text = model.Destination;
        CreateSubfolderCheck.IsChecked = model.CreateSubfolder;
        NamePartsList.ItemsSource = model.NameParts;
        AddPartCombo.ItemsSource = Enum.GetValues<NamePartKind>(); AddPartCombo.SelectedIndex = 0;
        SeparatorCombo.ItemsSource = Enum.GetValues<NamePartSeparator>(); SeparatorCombo.SelectedItem = model.Separator;
        ContainerCombo.ItemsSource = Enum.GetValues<ExportContainerChoice>(); ContainerCombo.SelectedItem = model.Container;
        CodecCombo.ItemsSource = Enum.GetValues<ExportCodecChoice>(); CodecCombo.SelectedItem = model.Codec;
        RateControlCombo.ItemsSource = Enum.GetValues<RateControlMode>(); RateControlCombo.SelectedItem = model.Encoding.RateControl;
        ResolutionCombo.ItemsSource = Enum.GetValues<OutputResolution>(); ResolutionCombo.SelectedItem = model.Resolution;
        FrameRateCombo.ItemsSource = new[] { "Same as Source", "23.976", "24", "25", "29.97", "30", "50", "59.94", "60" }; FrameRateCombo.SelectedIndex = 0;
        AudioCombo.ItemsSource = new[] { "Use source audio", "AAC", "No audio" }; AudioCombo.SelectedIndex = (int)model.Encoding.AudioMode;
        ParallelCombo.ItemsSource = Enumerable.Range(EncodingJobConcurrency.Minimum, EncodingJobConcurrency.Maximum).ToArray(); ParallelCombo.SelectedItem = model.ParallelExports;
        CameraCombo.ItemsSource = model.CameraChoices; CameraCombo.SelectedIndex = 0;
        CreativeCombo.ItemsSource = model.CreativeChoices; CreativeCombo.SelectedIndex = 0;
        ExistingCombo.SelectedIndex = 0;
        TuneCombo.ItemsSource = Enum.GetValues<EncoderTune>(); TuneCombo.SelectedItem = model.Encoding.Tune;
        MultipassCombo.ItemsSource = Enum.GetValues<MultipassMode>(); MultipassCombo.SelectedItem = model.Encoding.Multipass;
        PixelFormatCombo.ItemsSource = Enum.GetValues<VideoPixelFormat>(); PixelFormatCombo.SelectedItem = model.Encoding.PixelFormat;
        QualityText.Text = model.Encoding.Quality.ToString(); TargetText.Text = model.Encoding.TargetBitrateMbps.ToString(); MaxText.Text = model.Encoding.MaxBitrateMbps.ToString(); PresetText.Text = model.Encoding.EncoderPreset.ToString(); AqText.Text = model.Encoding.AqStrength.ToString();
        SpatialAqCheck.IsChecked = model.Encoding.SpatialAq; TemporalAqCheck.IsChecked = model.Encoding.TemporalAq; DeinterlaceCheck.IsChecked = model.Encoding.Deinterlace; FastStartCheck.IsChecked = model.Encoding.FastStart;
        _initializing = false; Sync(); Loaded += async (_, _) =>
        {
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

    private async Task InitializePreflightAsync()
    {
        var capabilities = await new EncoderCapabilityService(_ffprobe is null ? null : Path.Combine(Path.GetDirectoryName(_ffprobe)!, "ffmpeg.exe"), new FfmpegEncoderCapabilityProbe()).GetAsync();
        var nvenc = capabilities.Single(x => x.Backend == EncoderBackend.NvidiaNvenc);
        EncoderCombo.ItemsSource = capabilities.Where(x => x.State != EncoderCapabilityState.NotImplemented); EncoderCombo.SelectedItem = nvenc;
        EncoderDiagnostic.Text = nvenc.Diagnostic; _model.ApplyEncoderCapability(nvenc);
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
        if (SeparatorCombo.SelectedItem is NamePartSeparator separator) _model.Separator = separator;
        if (ContainerCombo.SelectedItem is ExportContainerChoice container) _model.Container = container;
        if (CodecCombo.SelectedItem is ExportCodecChoice codec) _model.Codec = codec;
        if (ResolutionCombo.SelectedItem is OutputResolution resolution) _model.Resolution = resolution;
        _model.ParallelExports = ParallelCombo.SelectedItem is int parallel ? parallel : EncodingJobConcurrency.Default;
        _model.OverwriteExisting = ExistingCombo.SelectedIndex == 1; _model.Camera = CameraCombo.SelectedItem as ExportLutChoice ?? _model.Camera; _model.Creative = CreativeCombo.SelectedItem as ExportLutChoice ?? _model.Creative;
        var frameRates = new[] { 0d, 23.976, 24, 25, 29.97, 30, 50, 59.94, 60 };
        var encoding = _model.Encoding with { RateControl = RateControlCombo.SelectedItem is RateControlMode rate ? rate : _model.Encoding.RateControl, FrameRate = frameRates[Math.Max(0, FrameRateCombo.SelectedIndex)], AudioMode = (AudioEncodingMode)Math.Max(0, AudioCombo.SelectedIndex), Tune = TuneCombo.SelectedItem is EncoderTune tune ? tune : _model.Encoding.Tune, Multipass = MultipassCombo.SelectedItem is MultipassMode pass ? pass : _model.Encoding.Multipass, PixelFormat = PixelFormatCombo.SelectedItem is VideoPixelFormat pixel ? pixel : _model.Encoding.PixelFormat, SpatialAq = SpatialAqCheck.IsChecked == true, TemporalAq = TemporalAqCheck.IsChecked == true, Deinterlace = DeinterlaceCheck.IsChecked == true, FastStart = FastStartCheck.IsChecked == true };
        if (int.TryParse(QualityText.Text, out var q)) encoding = encoding with { Quality = q }; if (int.TryParse(TargetText.Text, out var target)) encoding = encoding with { TargetBitrateMbps = target }; if (int.TryParse(MaxText.Text, out var max)) encoding = encoding with { MaxBitrateMbps = max }; if (int.TryParse(PresetText.Text, out var preset)) encoding = encoding with { EncoderPreset = preset }; if (int.TryParse(AqText.Text, out var aq)) encoding = encoding with { AqStrength = aq };
        _model.Encoding = encoding; _model.AdvancedExpanded = AdvancedExpander.IsExpanded; Sync();
    }
    private void Sync()
    {
        NamePreview.Text = _model.PreviewName; PathPreview.Text = _model.PreviewPath;
        QualityText.IsEnabled = _model.QualityAuthoritative; TargetText.IsEnabled = _model.TargetBitrateAuthoritative; MaxText.IsEnabled = _model.MaxBitrateAuthoritative;
        var lines = _model.Errors.Select(x => "Error — " + x.Message).Concat(_model.Warnings.Select(x => "Warning — " + x.Message)).ToList();
        PreflightText.Text = lines.Count == 0 ? (_model.CanExport ? "Ready to export." : "Analyzing sources and encoder availability…") : string.Join(Environment.NewLine, lines);
        ExportButton.IsEnabled = _model.CanExport;
    }
    private void Browse_Click(object sender, RoutedEventArgs e) { using var dialog = new Forms.FolderBrowserDialog { SelectedPath = DestinationText.Text }; if (dialog.ShowDialog() == Forms.DialogResult.OK) DestinationText.Text = dialog.SelectedPath; }
    private void AddPart_Click(object sender, RoutedEventArgs e) { if (AddPartCombo.SelectedItem is NamePartKind kind) _model.AddPart(kind); NamePartsList.Items.Refresh(); Sync(); }
    private void RemovePart_Click(object sender, RoutedEventArgs e) { _model.RemovePart(NamePartsList.SelectedIndex); Sync(); }
    private void MoveUp_Click(object sender, RoutedEventArgs e) { var i=NamePartsList.SelectedIndex; _model.MovePart(i,-1); NamePartsList.SelectedIndex=Math.Max(0,i-1); Sync(); }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { var i=NamePartsList.SelectedIndex; _model.MovePart(i,1); NamePartsList.SelectedIndex=Math.Min(_model.NameParts.Count-1,i+1); Sync(); }
    private void NamePart_Selected(object sender, SelectionChangedEventArgs e) { var part=NamePartsList.SelectedItem as NamePart; var custom=part?.Kind==NamePartKind.CustomText; CustomTextBox.Visibility=custom?Visibility.Visible:Visibility.Collapsed; if(custom) CustomTextBox.Text=part!.Text??""; }
    private void CustomText_Changed(object sender, TextChangedEventArgs e) { if (!_initializing && NamePartsList.SelectedIndex>=0) { _model.UpdateCustomText(NamePartsList.SelectedIndex, CustomTextBox.Text); Sync(); } }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        ExportButton.IsEnabled=false; QueueError.Text="";
        try { var plan=await _model.MaterializeAcceptedPlanAsync(); if(!plan.IsValid) { Sync(); return; } _coordinator.Queue(plan); DialogResult=true; }
        catch(Exception ex) { QueueError.Text=ex.Message; Sync(); }
    }
}
