using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace LightflowStudio;

public partial class MainWindow : Window
{
    private string? _ffmpeg;
    private string? _ffprobe;
    private CancellationTokenSource? _cts;
    private readonly BatchProgressState _batchProgress = new();
    private readonly string? _commandLineFolder;
    private AppSettings _settings = new();
    private AppState _state = new();
    private Process? _activeEncodingProcess;
    private readonly EncodingPauseController _encodingPause = new();
    private readonly ObservableCollection<BatchFileOption> _batchFiles = [];
    private readonly BatchFileSelectionMemory _batchSelectionMemory = new();
    private readonly ActivityLogFile _activityLogFile = App.ActivityLog;
    private readonly DispatcherTimer _batchFolderRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? _batchMetadataCts;
    private readonly Dictionary<ToggleButton, CancellationTokenSource> _requirementHelpDismissals = [];
    private Stopwatch? _batchStopwatch;
    private bool _closeAfterCurrent;
    private bool _forceClose;
    private bool _subfolderUsesResolutionDefault = true;
    private bool _updatingSubfolderName;
    private bool _filenameSuffixUsesResolutionDefault = true;
    private bool _updatingFilenameSuffix;
    private static readonly double[] FrameRateValues = [0, 23.976, 24, 25, 29.97, 30, 50, 59.94, 60];
    private static readonly int[] AudioSampleRates = [0, 44100, 48000, 96000];

    public MainWindow()
    {
        InitializeComponent();
        _batchFolderRefreshTimer.Tick += (_, _) =>
        {
            _batchFolderRefreshTimer.Stop();
            RefreshBatchFiles();
        };
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
        _commandLineFolder = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists);
        Loaded += async (_, _) =>
        {
            AboutVersionText.Text = $"Version {AppVersion.Display}  •  Built for the creative workflow";
            _settings = AppSettingsStore.Load(AppSettingsStore.SettingsPath);
            _state = AppStateStore.Load(AppStateStore.StatePath);
            PopulateSettingsControls(_settings);
            ApplySettingsToBatch(_settings);
            ApplyStateToBatch(_state);
            if (_commandLineFolder is not null) InputFolder.Text = _commandLineFolder;
            BatchFileList.ItemsSource = _batchFiles;
            LocateTools();
            await RefreshDependencyHealthAsync();
            RefreshBatchFiles();
            RefreshLuts();
        };
    }
    private void LocateTools(string? configuredPath = null)
    {
        var baseDir = AppContext.BaseDirectory;
        _ffmpeg = ExecutableLocator.Find("ffmpeg.exe", Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe"), configured: configuredPath ?? _settings.FfmpegPath);
        var besideFfmpeg = _ffmpeg is null ? "" : Path.Combine(Path.GetDirectoryName(_ffmpeg)!, "ffprobe.exe");
        _ffprobe = ExecutableLocator.Find("ffprobe.exe", Path.Combine(baseDir, "ffmpeg", "bin", "ffprobe.exe"), configured: besideFfmpeg);
        StatusText.Text = _ffmpeg is null ? "FFmpeg not found — configure it in Settings" : $"FFmpeg ready: {_ffmpeg}";
    }
    private async Task RefreshDependencyHealthAsync()
    {
        DependencySummary.Text = "Checking the tools needed for encoding…";
        DependencyResults.ItemsSource = null;
        var report = await DependencyHealthCheck.RunAsync(_ffmpeg, _ffprobe);
        DependencyResults.ItemsSource = report.Items;
        DependencySummary.Text = report.Summary;
        StatusText.Text = report.IsReady ? "Encoding tools ready" : "Encoding setup needs attention — open Settings";
        _activityLogFile.TryAppend($"[App] Dependency check: {report.Summary}");
    }

    private void RequirementHelp_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not ToggleButton button) return;
        if (_requirementHelpDismissals.Remove(button, out var pending)) pending.Cancel();
    }

    private async void RequirementHelp_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not ToggleButton button || button.IsChecked != true) return;
        if (_requirementHelpDismissals.Remove(button, out var pending)) pending.Cancel();
        var dismissal = new CancellationTokenSource();
        _requirementHelpDismissals[button] = dismissal;
        try
        {
            await Task.Delay(900, dismissal.Token);
            if (!button.IsMouseOver) button.IsChecked = false;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_requirementHelpDismissals.TryGetValue(button, out var current) && ReferenceEquals(current, dismissal))
                _requirementHelpDismissals.Remove(button);
            dismissal.Dispose();
        }
    }

    private void RequirementHelpPopup_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToggleButton button }) button.IsChecked = false;
    }
    private async void CheckDependencies_Click(object sender, RoutedEventArgs e)
    {
        LocateTools(SettingsFfmpegPath.Text);
        await RefreshDependencyHealthAsync();
    }
    private static string? PickFolder(string description, string? initialFolder = null)
    {
        using var dlg = new Forms.FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder)) dlg.SelectedPath = initialFolder;
        return dlg.ShowDialog() == Forms.DialogResult.OK ? dlg.SelectedPath : null;
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the folder containing video files", InputFolder.Text) is not { } folder) return;
        InputFolder.Text = folder;
        RefreshBatchFiles();
    }

    private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the output folder", OutputSpecificFolder.Text) is { } folder) OutputSpecificFolder.Text = folder;
    }


    private void OutputMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded) { UpdateOutputModeUi(); RefreshBatchFiles(); }
    }

    private void OutputDestination_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => RefreshBatchFiles();

    private void UpdateOutputModeUi()
    {
        var mode = (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2);
        OutputSameFolderPanel.Visibility = mode == OutputDestinationMode.SameFolder ? Visibility.Visible : Visibility.Collapsed;
        OutputSubfolderPanel.Visibility = mode == OutputDestinationMode.Subfolder ? Visibility.Visible : Visibility.Collapsed;
        OutputSpecificPanel.Visibility = mode == OutputDestinationMode.SpecificFolder ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreserveFolderStructureUi();
    }

    private void UpdatePreserveFolderStructureUi()
    {
        var mode = (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2);
        PreserveFolderStructure.Visibility = FolderStructurePolicy.IsAvailable(Recursive.IsChecked == true, mode)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    private void Resolution_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_subfolderUsesResolutionDefault) SetResolutionSubfolderName();
        if (_filenameSuffixUsesResolutionDefault) SetResolutionFilenameSuffix();
        RefreshBatchFiles();
    }

    private void OutputSubfolderName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsLoaded && !_updatingSubfolderName) _subfolderUsesResolutionDefault = false;
    }

    private void OutputFilenameSuffix_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsLoaded && !_updatingFilenameSuffix) _filenameSuffixUsesResolutionDefault = false;
    }

    private void SetResolutionFilenameSuffix()
    {
        _updatingFilenameSuffix = true;
        OutputFilenameSuffix.Text = $"_{EncodingPathPlanner.ResolutionName((OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5))}";
        _updatingFilenameSuffix = false;
        _filenameSuffixUsesResolutionDefault = true;
    }
    private void SetResolutionSubfolderName()
    {
        _updatingSubfolderName = true;
        OutputSubfolderName.Text = EncodingPathPlanner.ResolutionName((OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5));
        _updatingSubfolderName = false;
        _subfolderUsesResolutionDefault = true;
    }
    private void InputFolder_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _batchFolderRefreshTimer.Stop();
        _batchMetadataCts?.Cancel();
        RememberBatchFileSelection();
        _batchFiles.Clear();
        UpdateBatchFileSummary();
        _batchFolderRefreshTimer.Start();
    }
    private void Recursive_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdatePreserveFolderStructureUi();
        RefreshBatchFiles();
    }
    private void SettingsRecursive_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SettingsPreserveFolderStructure.Visibility = SettingsRecursive.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    private void RefreshBatchFiles_Click(object sender, RoutedEventArgs e) => RefreshBatchFiles();
    private void BatchFileSelection_Click(object sender, RoutedEventArgs e)
    {
        RememberBatchFileSelection();
        UpdateBatchFileSummary();
    }
    private void SelectAllBatchFiles_Click(object sender, RoutedEventArgs e) => SetBatchFileSelection(true);
    private void SelectNoBatchFiles_Click(object sender, RoutedEventArgs e) => SetBatchFileSelection(false);

    private void RefreshBatchFiles()
    {
        _batchFolderRefreshTimer.Stop();
        RememberBatchFileSelection();
        _batchMetadataCts?.Cancel();
        _batchMetadataCts?.Dispose();
        _batchMetadataCts = new CancellationTokenSource();
        _batchFiles.Clear();
        string? excludedOutput = null;
        try
        {
            if ((OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2) != OutputDestinationMode.SameFolder)
            {
                var candidate = OutputDestinationPlanner.ResolveRoot(InputFolder.Text, (OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5), CurrentOutputDestination());
                if (!string.Equals(Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(InputFolder.Text).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) excludedOutput = candidate;
            }
        }
        catch (ArgumentException) { }
        string? excludedSuffix = null;
        try
        {
            if ((OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2) == OutputDestinationMode.SameFolder)
                excludedSuffix = OutputDestinationPlanner.ResolveFilenameSuffix((OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5), CurrentOutputDestination());
        }
        catch (ArgumentException) { }
        foreach (var option in BatchFileSelection.Discover(InputFolder.Text, Recursive.IsChecked == true, excludedOutput, excludedSuffix))
            _batchFiles.Add(option);
        _batchSelectionMemory.Apply(InputFolder.Text, _batchFiles);
        UpdateBatchFileSummary();
        _ = LoadBatchMetadataAsync(_batchFiles.ToList(), _batchMetadataCts.Token);
    }

    private async Task LoadBatchMetadataAsync(IReadOnlyList<BatchFileOption> options, CancellationToken token)
    {
        if (_ffprobe is null)
        {
            foreach (var option in options) option.MarkMetadataUnavailable();
            MediaWarningAnalyzer.Apply(options);
            UpdateBatchFileSummary();
            return;
        }

        try
        {
            foreach (var option in options)
            {
                token.ThrowIfCancellationRequested();
                var result = await CaptureAsync(_ffprobe, FfmpegCommandBuilder.ProbeMetadata(option.FilePath), token);
                if (result.ExitCode == 0 && MediaMetadataParser.TryParse(result.StdOut, option.FileSizeBytes, out var metadata))
                    option.ApplyMetadata(metadata);
                else
                    option.MarkMetadataUnavailable();
                MediaWarningAnalyzer.Apply(options);
                UpdateBatchFileSummary();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer folder selection replaced this analysis.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _activityLogFile.TryAppend($"[App] Metadata probe failed: {ex}");
            foreach (var option in options.Where(item => item.IsAnalyzing)) option.MarkMetadataUnavailable();
            MediaWarningAnalyzer.Apply(options);
            UpdateBatchFileSummary();
        }
    }

    private void SetBatchFileSelection(bool selected)
    {
        foreach (var option in _batchFiles) option.IsSelected = selected;
        RememberBatchFileSelection();
        UpdateBatchFileSummary();
    }

    private void RememberBatchFileSelection()
    {
        if (_batchFiles.Count > 0) _batchSelectionMemory.Remember(InputFolder.Text, _batchFiles);
    }

    private void UpdateBatchFileSummary()
    {
        BatchFileSummary.Text = BatchFileSelection.Summary(_batchFiles);
        UpdateBatchReadiness();
    }

    private void UpdateBatchReadiness(bool updateGuidance = true)
    {
        var folder = InputFolder.Text.Trim();
        var selected = _batchFiles.Count(file => file.IsSelected);
        var presentation = BatchStartReadiness.Evaluate(folder.Length > 0, Directory.Exists(folder), _batchFiles.Count, selected);
        StartButton.IsEnabled = _cts is null && presentation.CanStart;
        if (updateGuidance) CurrentFileText.Text = presentation.Guidance;
    }

    private void RefreshLuts_Click(object sender, RoutedEventArgs e) => RefreshLuts();

    private int RefreshLuts()
    {
        var previousSelection = LutSelection.SelectedItem as LutOption;
        var preferredPath = previousSelection?.FilePath ?? _state.LastLutPath;
        var luts = LutCatalog.Discover(_settings.LutFolder);
        var options = new List<LutOption> { LutCatalog.NoLut };
        options.AddRange(luts);
        LutSelection.ItemsSource = options;
        LutSelection.SelectedItem = options.FirstOrDefault(option =>
            string.Equals(option.FilePath, preferredPath, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        SettingsMessage.Text = luts.Count == 0
            ? $"No .cube LUT files found in {_settings.LutFolder}. Encoding will run without a LUT."
            : $"Loaded {luts.Count} LUT{(luts.Count == 1 ? "" : "s")}.";
        return luts.Count;
    }

    private void LutSelection_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SelectedLutPath is not { } path || string.Equals(path, _state.LastLutPath, StringComparison.OrdinalIgnoreCase)) return;
        _state = _state with { LastLutPath = path };
        try
        {
            AppStateStore.Save(AppStateStore.StatePath, _state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _activityLogFile.TryAppend($"[App] Could not remember LUT selection: {ex}");
            SettingsMessage.Text = $"Could not remember LUT selection: {ex.Message}";
        }
    }

    private void BrowseDefaultVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the default video folder", SettingsDefaultVideoFolder.Text) is { } folder)
            SettingsDefaultVideoFolder.Text = folder;
    }

    private void BrowseSettingsLutFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Select the folder containing .cube LUT files", SettingsLutFolder.Text) is { } folder)
            SettingsLutFolder.Text = folder;
    }

    private void BrowseSettingsFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "FFmpeg executable|ffmpeg.exe", Title = "Select ffmpeg.exe" };
        if (File.Exists(SettingsFfmpegPath.Text)) dialog.InitialDirectory = Path.GetDirectoryName(SettingsFfmpegPath.Text);
        if (dialog.ShowDialog() == true) SettingsFfmpegPath.Text = dialog.FileName;
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadEncodingControls(out var encoding, out var encodingError))
        {
            MessageBox.Show(encodingError, "Encoding settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var settings = ReadSettingsControls(encoding);
        if (!string.IsNullOrWhiteSpace(settings.FfmpegPath) && !File.Exists(settings.FfmpegPath))
        {
            MessageBox.Show("The configured FFmpeg executable does not exist.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            AppSettingsStore.Save(AppSettingsStore.SettingsPath, settings);
            _settings = settings;
            ApplySettingsToBatch(settings);
            LocateTools();
            await RefreshDependencyHealthAsync();
            RefreshBatchFiles();
            var lutCount = RefreshLuts();
            SettingsMessage.Text = $"Settings saved. {lutCount} LUT{(lutCount == 1 ? "" : "s")} available.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _activityLogFile.TryAppend($"[App] Could not save settings: {ex}");
            MessageBox.Show(ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        PopulateSettingsControls(new AppSettings());
        SettingsMessage.Text = "Default values loaded. Select Save Settings to apply them.";
    }

    private AppSettings ReadSettingsControls(EncodingOptions encoding)
    {
        var selectedPreset = (EncodingPreset)Math.Clamp(SettingsEncodingPreset.SelectedIndex, 0, 4);
        if (selectedPreset != EncodingPreset.Custom && encoding != EncodingPresetCatalog.Get(selectedPreset))
            selectedPreset = EncodingPreset.Custom;
        return AppSettings.Normalize(new AppSettings
        {
            DefaultVideoFolder = SettingsDefaultVideoFolder.Text,
            LutFolder = SettingsLutFolder.Text,
            FfmpegPath = SettingsFfmpegPath.Text,
            DefaultResolution = (OutputResolution)SettingsResolution.SelectedIndex,
            DefaultRecovery = (RecoveryStrategy)SettingsRecoveryMode.SelectedIndex,
            IncludeSubfolders = SettingsRecursive.IsChecked == true,
            PreserveFolderStructure = SettingsPreserveFolderStructure.IsChecked == true,
            OverwriteExistingFiles = SettingsOverwriteExisting.IsChecked == true,
            DetailedActivityLogging = ShowEncodingDetails.IsChecked == true,
            EncodingPreset = selectedPreset,
            Encoding = encoding
        });
    }

    private bool TryReadEncodingControls(out EncodingOptions options, out string error)
    {
        options = EncodingPresetCatalog.Recommended;
        error = "";
        if (!TryReadInt(SettingsEncoderPreset.Text, "NVENC preset", out var encoderPreset)
            || !TryReadInt(SettingsQuality.Text, "Quality", out var quality)
            || !TryReadInt(SettingsTargetBitrate.Text, "Target bitrate", out var targetBitrate)
            || !TryReadInt(SettingsMaxBitrate.Text, "Maximum bitrate", out var maxBitrate)
            || !TryReadInt(SettingsAqStrength.Text, "AQ strength", out var aqStrength)
            || !TryReadInt(SettingsAudioBitrate.Text, "AAC bitrate", out var audioBitrate))
        {
            error = _numericSettingError;
            return false;
        }

        options = new EncodingOptions
        {
            Backend = EncoderBackend.NvidiaNvenc,
            Codec = (VideoCodec)SettingsVideoCodec.SelectedIndex,
            EncoderPreset = encoderPreset,
            Tune = (EncoderTune)SettingsTune.SelectedIndex,
            RateControl = (RateControlMode)SettingsRateControl.SelectedIndex,
            Quality = quality,
            TargetBitrateMbps = targetBitrate,
            MaxBitrateMbps = maxBitrate,
            Multipass = (MultipassMode)SettingsMultipass.SelectedIndex,
            SpatialAq = SettingsSpatialAq.IsChecked == true,
            TemporalAq = SettingsTemporalAq.IsChecked == true,
            AqStrength = aqStrength,
            PixelFormat = (VideoPixelFormat)SettingsPixelFormat.SelectedIndex,
            FrameRate = FrameRateValues[Math.Clamp(SettingsFrameRate.SelectedIndex, 0, FrameRateValues.Length - 1)],
            Deinterlace = SettingsDeinterlace.IsChecked == true,
            AudioMode = (AudioEncodingMode)SettingsAudioMode.SelectedIndex,
            AudioBitrateKbps = audioBitrate,
            AudioSampleRate = AudioSampleRates[Math.Clamp(SettingsAudioSampleRate.SelectedIndex, 0, AudioSampleRates.Length - 1)],
            AudioChannels = Math.Clamp(SettingsAudioChannels.SelectedIndex, 0, 2),
            Container = (OutputContainer)SettingsContainer.SelectedIndex,
            FastStart = SettingsFastStart.IsChecked == true
        };
        var errors = EncodingOptionValidator.Validate(options);
        if (errors.Count == 0) return true;
        error = string.Join(Environment.NewLine, errors);
        return false;
    }

    private string _numericSettingError = "";
    private bool TryReadInt(string text, string label, out int value)
    {
        if (int.TryParse(text, out value)) return true;
        _numericSettingError = $"{label} must be a whole number.";
        return false;
    }

    private void ApplyEncodingPreset_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsEncodingPreset.SelectedIndex == (int)EncodingPreset.Custom)
        {
            SettingsMessage.Text = "Custom settings are already displayed; choose a named preset to replace them.";
            return;
        }
        var preset = (EncodingPreset)Math.Clamp(SettingsEncodingPreset.SelectedIndex, 0, 3);
        PopulateEncodingControls(EncodingPresetCatalog.Get(preset));
        SettingsMessage.Text = $"{SettingsEncodingPreset.Text} preset loaded. Select Save Settings to apply it.";
    }
    private void PopulateSettingsControls(AppSettings settings)
    {
        SettingsDefaultVideoFolder.Text = settings.DefaultVideoFolder;
        SettingsLutFolder.Text = settings.LutFolder;
        SettingsFfmpegPath.Text = settings.FfmpegPath;
        SettingsResolution.SelectedIndex = (int)settings.DefaultResolution;
        SettingsRecoveryMode.SelectedIndex = (int)settings.DefaultRecovery;
        SettingsRecursive.IsChecked = settings.IncludeSubfolders;
        SettingsPreserveFolderStructure.IsChecked = settings.PreserveFolderStructure;
        SettingsOverwriteExisting.IsChecked = settings.OverwriteExistingFiles;
        ShowEncodingDetails.IsChecked = settings.DetailedActivityLogging;
        SettingsEncodingPreset.SelectedIndex = (int)settings.EncodingPreset;
        PopulateEncodingControls(settings.Encoding);
    }

    private void PopulateEncodingControls(EncodingOptions options)
    {
        SettingsEncoderBackend.SelectedIndex = 0;
        SettingsVideoCodec.SelectedIndex = (int)options.Codec;
        SettingsContainer.SelectedIndex = (int)options.Container;
        SettingsAudioMode.SelectedIndex = (int)options.AudioMode;
        SettingsEncoderPreset.Text = options.EncoderPreset.ToString();
        SettingsTune.SelectedIndex = (int)options.Tune;
        SettingsRateControl.SelectedIndex = (int)options.RateControl;
        SettingsMultipass.SelectedIndex = (int)options.Multipass;
        SettingsQuality.Text = options.Quality.ToString();
        SettingsTargetBitrate.Text = options.TargetBitrateMbps.ToString();
        SettingsMaxBitrate.Text = options.MaxBitrateMbps.ToString();
        SettingsAqStrength.Text = options.AqStrength.ToString();
        SettingsPixelFormat.SelectedIndex = (int)options.PixelFormat;
        SettingsFrameRate.SelectedIndex = Array.IndexOf(FrameRateValues, options.FrameRate) is var frameIndex && frameIndex >= 0 ? frameIndex : 0;
        SettingsAudioBitrate.Text = options.AudioBitrateKbps.ToString();
        SettingsAudioSampleRate.SelectedIndex = Array.IndexOf(AudioSampleRates, options.AudioSampleRate) is var sampleIndex && sampleIndex >= 0 ? sampleIndex : 0;
        SettingsAudioChannels.SelectedIndex = options.AudioChannels;
        SettingsDeinterlace.IsChecked = options.Deinterlace;
        SettingsSpatialAq.IsChecked = options.SpatialAq;
        SettingsTemporalAq.IsChecked = options.TemporalAq;
        SettingsFastStart.IsChecked = options.FastStart;
    }

    private void ApplySettingsToBatch(AppSettings settings)
    {
        InputFolder.Text = settings.DefaultVideoFolder;
        Resolution.SelectedIndex = (int)settings.DefaultResolution;
        RecoveryMode.SelectedIndex = (int)settings.DefaultRecovery;
        Recursive.IsChecked = settings.IncludeSubfolders;
        PreserveFolderStructure.IsChecked = settings.PreserveFolderStructure;
        OverwriteExisting.IsChecked = settings.OverwriteExistingFiles;
        OutputMode.SelectedIndex = (int)OutputDestinationMode.Subfolder;
        OutputSpecificFolder.Text = "";
        SetResolutionSubfolderName();
        SetResolutionFilenameSuffix();
        UpdateOutputModeUi();
        if (IsLoaded) RefreshBatchFiles();
    }

    private void ApplyStateToBatch(AppState state)
    {
        if (!state.HasBatchState) return;
        InputFolder.Text = state.LastVideoFolder;
        Resolution.SelectedIndex = (int)state.LastResolution;
        RecoveryMode.SelectedIndex = (int)state.LastRecovery;
        Recursive.IsChecked = state.LastIncludeSubfolders;
        PreserveFolderStructure.IsChecked = state.LastPreserveFolderStructure;
        OverwriteExisting.IsChecked = state.LastOverwriteExistingFiles;
        OutputMode.SelectedIndex = (int)state.LastOutputMode;
        OutputSpecificFolder.Text = state.LastSpecificOutputFolder;
        _subfolderUsesResolutionDefault = state.LastOutputSubfolderUsesResolutionDefault;
        _filenameSuffixUsesResolutionDefault = state.LastFilenameSuffixUsesResolutionDefault;
        if (_subfolderUsesResolutionDefault) SetResolutionSubfolderName();
        else OutputSubfolderName.Text = state.LastOutputSubfolder;
        if (_filenameSuffixUsesResolutionDefault) SetResolutionFilenameSuffix();
        else OutputFilenameSuffix.Text = state.LastFilenameSuffix;
        UpdateOutputModeUi();
    }

    private void SaveBatchState()
    {
        _state = _state with
        {
            HasBatchState = true,
            LastVideoFolder = InputFolder.Text,
            LastResolution = (OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5),
            LastRecovery = (RecoveryStrategy)Math.Clamp(RecoveryMode.SelectedIndex, 0, 2),
            LastIncludeSubfolders = Recursive.IsChecked == true,
            LastPreserveFolderStructure = PreserveFolderStructure.IsChecked == true,
            LastOverwriteExistingFiles = OverwriteExisting.IsChecked == true,
            LastOutputMode = (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2),
            LastOutputSubfolder = OutputSubfolderName.Text,
            LastOutputSubfolderUsesResolutionDefault = _subfolderUsesResolutionDefault,
            LastFilenameSuffix = OutputFilenameSuffix.Text,
            LastFilenameSuffixUsesResolutionDefault = _filenameSuffixUsesResolutionDefault,
            LastSpecificOutputFolder = OutputSpecificFolder.Text
        };
        try { AppStateStore.Save(AppStateStore.StatePath, _state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppendDetailedLog($"Could not remember batch choices: {ex.Message}"); }
    }

    private OutputDestinationOptions CurrentOutputDestination() => new(
        (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2),
        _subfolderUsesResolutionDefault ? "" : OutputSubfolderName.Text,
        OutputSpecificFolder.Text,
        _filenameSuffixUsesResolutionDefault ? "" : OutputFilenameSuffix.Text);

    private bool ShouldPreserveFolderStructure() => FolderStructurePolicy.ShouldPreserve(
        Recursive.IsChecked == true,
        (OutputDestinationMode)Math.Clamp(OutputMode.SelectedIndex, 0, 2),
        PreserveFolderStructure.IsChecked == true);
    private void BrowseMedia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Video files|*.mp4;*.mov;*.mxf;*.mkv;*.avi|All files|*.*" };
        if (Directory.Exists(_settings.DefaultVideoFolder)) dialog.InitialDirectory = _settings.DefaultVideoFolder;
        if (dialog.ShowDialog() == true) MediaPath.Text = dialog.FileName;
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateEncoderInputs()) return;
        SaveBatchState();
        _closeAfterCurrent = false;
        _cts = new CancellationTokenSource();
        ToggleEncoding(true);

        var total = 0;
        var encoded = 0;
        var failed = 0;
        var skipped = 0;
        var outputRoot = "";
        var outcome = "completed";
        Stopwatch? batchStart = null;

        try
        {
            var files = BatchFileSelection.SelectedFiles(_batchFiles);
            if (files.Count == 0) throw new InvalidOperationException("Select at least one video file for this batch.");
            total = files.Count;
            _batchProgress.StartBatch(total);
            ApplyProgressState();

            var recovery = (RecoveryStrategy)RecoveryMode.SelectedIndex;
            var resolution = (OutputResolution)Resolution.SelectedIndex;
            outputRoot = OutputDestinationPlanner.ResolveRoot(InputFolder.Text, resolution, CurrentOutputDestination());
            Directory.CreateDirectory(outputRoot);
            batchStart = Stopwatch.StartNew();
            _batchStopwatch = batchStart;
            var startedAt = DateTime.Now;

            CurrentFileText.Text = $"Analyzing {total} file{(total == 1 ? "" : "s")}…";
            AppendLog($"Preparing batch — {total} file{(total == 1 ? "" : "s")} discovered.");
            var durations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var analyzedDuration = _batchFiles.FirstOrDefault(option =>
                    string.Equals(option.FilePath, file, StringComparison.OrdinalIgnoreCase))?.Metadata?.DurationSeconds ?? 0;
                durations[file] = analyzedDuration > 0 ? analyzedDuration : await ProbeDurationAsync(file, _cts.Token);
            }
            var sourceDuration = TimeSpan.FromSeconds(durations.Values.Where(value => value > 0).Sum());
            AppendLog(BatchLogFormatter.Started(total, outputRoot, resolution, recovery, sourceDuration, startedAt));
            AppendDetailedLog($"LUT: {(string.IsNullOrEmpty(SelectedLutPath) ? "None" : SelectedLutPath)}");
            AppendDetailedLog($"Input folder: {InputFolder.Text}");
            AppendDetailedLog($"Encoder: {_settings.Encoding.Codec} via NVIDIA NVENC; preset P{_settings.Encoding.EncoderPreset}; {_settings.Encoding.RateControl}; {_settings.Encoding.Container}");
            AppendDetailedLog($"Scanning subfolders: {(Recursive.IsChecked == true ? "Yes" : "No")}; preserve folder structure: {(ShouldPreserveFolderStructure() ? "Yes" : "No")}; overwrite existing files: {(OverwriteExisting.IsChecked == true ? "Yes" : "No")}");

            var completed = 0;
            foreach (var input in files)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var suffix = OutputDestinationPlanner.ResolveFilenameSuffix(resolution, CurrentOutputDestination());
                var job = EncodingPathPlanner.CreateJob(InputFolder.Text, outputRoot, input, resolution, _settings.Encoding.Container, suffix, ShouldPreserveFolderStructure());
                var outDir = Path.GetDirectoryName(job.OutputPath)!;
                Directory.CreateDirectory(outDir);
                var output = job.OutputPath;
                _batchProgress.StartFile();
                FileProgress.Value = _batchProgress.FilePercent;
                CurrentFileText.Text = $"{completed + 1}/{total}: {Path.GetFileName(input)}";
                AppendDetailedLog($"File {completed + 1} of {total}: {input}");
                AppendDetailedLog($"Output: {output}");
                AppendDetailedLog($"Detected duration: {FormatDuration(durations[input])}");

                var outputInfo = new FileInfo(output);
                if (ExistingOutputPolicy.ShouldPreserve(OverwriteExisting.IsChecked == true, outputInfo.Exists, outputInfo.Exists ? outputInfo.Length : 0))
                {
                    skipped++;
                    completed++;
                    AppendLog($"Preserved existing file: {output}");
                    UpdateBatch(completed, total, batchStart);
                    if (_closeAfterCurrent)
                    {
                        outcome = "stopped after current file";
                        break;
                    }
                    continue;
                }

                var detailedOutput = ShowEncodingDetails.IsChecked == true;
                var args = FfmpegCommandBuilder.Encode(input, output, SelectedLutPath, recovery, resolution, detailedOutput, _settings.Encoding);
                AppendDetailedLog($"Starting FFmpeg: {FormatCommand(_ffmpeg!, args)}");
                var exit = await RunFfmpegProgressAsync(args, durations[input], detailedOutput, p =>
                {
                    _batchProgress.ReportFileProgress(p);
                    FileProgress.Value = _batchProgress.FilePercent;
                    UpdateBatch(completed, total, batchStart, p);
                }, _cts.Token);
                if (exit == 0)
                {
                    encoded++;
                    AppendLog($"Completed: {output}");
                }
                else
                {
                    failed++;
                    AppendLog($"FAILED ({exit}): {input}");
                }

                completed++;
                UpdateBatch(completed, total, batchStart);
                if (_closeAfterCurrent)
                {
                    outcome = "stopped after current file";
                    break;
                }
            }

            CurrentFileText.Text = outcome == "completed" ? "Batch complete" : "Current file complete — closing";
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            AppendLog("Encoding cancelled.");
            CurrentFileText.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            outcome = "failed";
            MessageBox.Show(ex.Message, "Encoding error", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog(ex.ToString());
        }
        finally
        {
            if (batchStart is not null && total > 0)
                AppendLog(BatchLogFormatter.Finished(outcome, total, encoded, failed, skipped, batchStart.Elapsed, outputRoot));

            var shouldClose = _closeAfterCurrent;
            _batchStopwatch = null;
            _closeAfterCurrent = false;
            _cts.Dispose();
            _cts = null;
            ToggleEncoding(false);
            if (shouldClose)
            {
                _forceClose = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }
    }
    private bool ValidateEncoderInputs()
    {
        if (_ffmpeg is null || !File.Exists(_ffmpeg)) { MessageBox.Show("FFmpeg was not found. Open Settings to configure ffmpeg.exe."); return false; }
        if (!Directory.Exists(InputFolder.Text)) { MessageBox.Show("Select a valid video folder."); return false; }
        if (_batchFiles.All(file => !file.IsSelected)) { MessageBox.Show("Select at least one video file for this batch."); return false; }
        try
        {
            var outputOptions = CurrentOutputDestination();
            var outputResolution = (OutputResolution)Math.Clamp(Resolution.SelectedIndex, 0, 5);
            var outputRoot = OutputDestinationPlanner.ResolveRoot(InputFolder.Text, outputResolution, outputOptions);
            var suffix = OutputDestinationPlanner.ResolveFilenameSuffix(outputResolution, outputOptions);
            var jobs = BatchFileSelection.SelectedFiles(_batchFiles)
                .Select(file => EncodingPathPlanner.CreateJob(InputFolder.Text, outputRoot, file, outputResolution,
                    _settings.Encoding.Container, suffix, ShouldPreserveFolderStructure()));
            if (EncodingPathPlanner.HasOutputCollisions(jobs))
                throw new ArgumentException("Multiple selected files would create the same output filename. Choose Same folder or Specific folder with Preserve source folder structure, or rename the source files.");
        }
        catch (ArgumentException ex) { MessageBox.Show(ex.Message, "Output location", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        if (SelectedLutPath is { Length: > 0 } lut && (!File.Exists(lut) || !lut.EndsWith(".cube", StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("Select a valid .cube LUT from the LUT dropdown, or choose No LUT."); return false; }
        return true;
    }

    private string? SelectedLutPath => (LutSelection.SelectedItem as LutOption)?.FilePath;


    private async Task<int> RunFfmpegProgressAsync(List<string> args, double duration, bool detailedOutput, Action<double> progress, CancellationToken token)
    {
        using var process = StartProcess(_ffmpeg!, args, redirectError: true);
        _activeEncodingProcess = process;
        PauseButton.IsEnabled = true;
        PauseButton.Content = "Pause";
        try
        {
            var errors = new StringBuilder();
            var errTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(token) is { } line)
                {
                    errors.AppendLine(line);
                    _activityLogFile.TryAppend($"[FFmpeg] {line}");
                    if (detailedOutput) ShowInActivityLog($"[FFmpeg] {line}");
                }
            }, token);
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (FfmpegProgressParser.TryParsePercent(line, duration, out var percent)) progress(percent);
            }
            await process.WaitForExitAsync(token);
            await errTask;
            if (process.ExitCode != 0 && !detailedOutput) ShowInActivityLog(errors.ToString());
            progress(100);
            return process.ExitCode;
        }
        finally
        {
            _encodingPause.Clear();
            PauseButton.IsEnabled = false;
            PauseButton.Content = "Pause";
            if (ReferenceEquals(_activeEncodingProcess, process)) _activeEncodingProcess = null;
        }
    }
    private void UpdateBatch(int completed, int total, Stopwatch sw, double currentFilePercent = 0)
    {
        _batchProgress.ReportBatchProgress(completed + Math.Clamp(currentFilePercent, 0, 100) / 100d, total);
        BatchProgress.Value = _batchProgress.BatchPercent;
        var remaining = BatchEtaEstimator.Estimate(sw.Elapsed, completed, total, currentFilePercent);
        EtaText.Text = remaining is null
            ? $"Completed {completed} of {total} — estimated remaining: calculating…"
            : $"Completed {completed} of {total} — estimated remaining: {remaining:hh\\:mm\\:ss}";
    }
    private void ApplyProgressState()
    {
        BatchProgress.Value = _batchProgress.BatchPercent;
        FileProgress.Value = _batchProgress.FilePercent;
        EtaText.Text = _batchProgress.StatusText;
    }
    private void ToggleEncoding(bool running)
    {
        BatchConfiguration.IsEnabled = !running;
        if (running) StartButton.IsEnabled = false;
        else UpdateBatchReadiness(updateGuidance: false);
        CancelButton.IsEnabled = running;
        if (!running)
        {
            PauseButton.IsEnabled = false;
            PauseButton.Content = "Pause";
        }
        SetBatchStatus(running ? BatchStatus.Encoding : BatchStatus.Ready);
    }

    private void SetBatchStatus(BatchStatus status)
    {
        var presentation = BatchStatusPresentation.For(status);
        BatchStateText.Text = presentation.Text;
        BatchStateText.Foreground = (System.Windows.Media.Brush)FindResource(presentation.ForegroundResource);
        BatchStateBorder.Background = (System.Windows.Media.Brush)FindResource(presentation.BackgroundResource);
        BatchStateBorder.BorderBrush = (System.Windows.Media.Brush)FindResource(presentation.BorderResource);
    }
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        var process = _activeEncodingProcess;
        if (process is null) return;

        if (_encodingPause.IsPaused)
        {
            ResumeEncoding(process, "Encoding resumed by user.");
            return;
        }

        if (!_encodingPause.Pause(process)) return;
        if (_batchStopwatch?.IsRunning == true) _batchStopwatch.Stop();
        PauseButton.Content = "Resume";
        SetBatchStatus(BatchStatus.Paused);
        AppendLog("Encoding paused by user.");
    }

    private void ResumeEncoding(Process? process, string logMessage)
    {
        _encodingPause.Resume(process);
        if (_batchStopwatch?.IsRunning == false) _batchStopwatch.Start();
        PauseButton.Content = "Pause";
        SetBatchStatus(BatchStatus.Encoding);
        AppendLog(logMessage);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelActiveEncoding();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveBatchState();
        if (_cts is null || _forceClose) return;

        e.Cancel = true;
        var pausedProcess = _activeEncodingProcess;
        var wasAlreadyPaused = _encodingPause.IsPaused;
        var processPaused = wasAlreadyPaused || _encodingPause.Pause(pausedProcess);
        var pausedByDialog = processPaused && !wasAlreadyPaused;
        if (pausedByDialog && _batchStopwatch?.IsRunning == true) _batchStopwatch.Stop();
        if (pausedByDialog)
        {
            SetBatchStatus(BatchStatus.Paused);
            PauseButton.Content = "Resume";
            AppendDetailedLog("Encoding paused while the close options are open.");
        }

        var dialog = new EncodingCloseDialog { Owner = this };
        dialog.ShowDialog();
        if (dialog.Choice == EncodingCloseChoice.CloseNow)
        {
            _forceClose = true;
            CancelActiveEncoding();
            _encodingPause.Clear();
            _ = Dispatcher.BeginInvoke(new Action(Close));
            return;
        }

        if (processPaused && EncodingClosePolicy.ShouldResumeAfterDialog(wasAlreadyPaused, dialog.Choice))
        {
            ResumeEncoding(pausedProcess, dialog.Choice == EncodingCloseChoice.CloseAfterCurrent
                ? "Encoding resumed to finish the current file before closing."
                : "Encoding resumed.");
        }

        if (dialog.Choice == EncodingCloseChoice.CloseAfterCurrent)
        {
            _closeAfterCurrent = true;
            CurrentFileText.Text = "Will close after the current file finishes";
            AppendLog("Close requested — the application will close after the current file finishes.");
        }
    }

    private void CancelActiveEncoding()
    {
        _cts?.Cancel();
        try
        {
            if (_activeEncodingProcess is { HasExited: false } process) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and the kill request.
        }
    }
    private void AppendLog(string text)
    {
        _activityLogFile.TryAppend(text);
        ShowInActivityLog(text);
    }

    private void ShowInActivityLog(string text)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.Text = ActivityLog.Append(LogBox.Text, text);
            LogBox.CaretIndex = LogBox.Text.Length;
            LogBox.ScrollToEnd();
        });
    }

    private void AppendDetailedLog(string text)
    {
        var line = $"[App] {text}";
        _activityLogFile.TryAppend(line);
        if (ShowEncodingDetails.IsChecked == true) ShowInActivityLog(line);
    }

    private void ShowEncodingDetails_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings = _settings with { DetailedActivityLogging = ShowEncodingDetails.IsChecked == true };
        try
        {
            AppSettingsStore.Save(AppSettingsStore.SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLog($"Could not save the encoding-details preference: {ex.Message}");
        }
    }
    private static string FormatDuration(double seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff") : "Unavailable";

    private static string FormatCommand(string executable, IEnumerable<string> args) =>
        QuoteCommandArgument(executable) + " " + string.Join(" ", args.Select(QuoteCommandArgument));

    private static string QuoteCommandArgument(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;

    private async Task<double> ProbeDurationAsync(string file, CancellationToken token)
    {
        if (_ffprobe is null) return 0;
        var result = await CaptureAsync(_ffprobe, FfmpegCommandBuilder.ProbeDuration(file), token);
        return double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureProbe(); var r = await CaptureLoggedAsync("Inspect", _ffprobe!, FfmpegCommandBuilder.Inspect(MediaPath.Text), CancellationToken.None); ToolsOutput.Text = r.StdOut + r.StdErr;
    });

    private async void Verify_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureMedia(); EnsureFfmpeg(); ToolsOutput.Text = "Verifying every decodable frame…";
        var r = await CaptureLoggedAsync("Verify", _ffmpeg!, FfmpegCommandBuilder.Verify(MediaPath.Text), CancellationToken.None);
        var report = Path.Combine(Path.GetDirectoryName(MediaPath.Text)!, Path.GetFileNameWithoutExtension(MediaPath.Text) + "_verification.csv");
        var status = r.ExitCode == 0 ? "completed" : "failed";
        File.WriteAllText(report, "file,status,exit_code,notes\r\n" + CsvFormatter.Escape(MediaPath.Text) + $",{status},{r.ExitCode}," + CsvFormatter.Escape(r.StdErr));
        ToolsOutput.Text = $"Verification {status}. Report: {report}\r\n\r\n{r.StdErr}";
    });

    private async void Rewrap_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureMedia(); EnsureFfmpeg(); var output = Path.Combine(Path.GetDirectoryName(MediaPath.Text)!, Path.GetFileNameWithoutExtension(MediaPath.Text) + "_rewrapped.mp4");
        var r = await CaptureLoggedAsync("Rewrap", _ffmpeg!, FfmpegCommandBuilder.Rewrap(MediaPath.Text, output), CancellationToken.None);
        ToolsOutput.Text = r.ExitCode == 0 ? $"Created: {output}" : r.StdErr;
    });

    private async void Proxy_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureMedia(); EnsureFfmpeg(); var output = Path.Combine(Path.GetDirectoryName(MediaPath.Text)!, Path.GetFileNameWithoutExtension(MediaPath.Text) + "_proxy.mp4");
        var r = await CaptureLoggedAsync("Proxy", _ffmpeg!, FfmpegCommandBuilder.Proxy(MediaPath.Text, output), CancellationToken.None);
        ToolsOutput.Text = r.ExitCode == 0 ? $"Created: {output}" : r.StdErr;
    });

    private async void ContactSheet_Click(object sender, RoutedEventArgs e) => await ToolAction(async () =>
    {
        EnsureMedia(); EnsureFfmpeg(); var output = Path.Combine(Path.GetDirectoryName(MediaPath.Text)!, Path.GetFileNameWithoutExtension(MediaPath.Text) + "_contact-sheet.jpg");
        var r = await CaptureLoggedAsync("ContactSheet", _ffmpeg!, FfmpegCommandBuilder.ContactSheet(MediaPath.Text, output), CancellationToken.None);
        ToolsOutput.Text = r.ExitCode == 0 ? $"Created: {output}" : r.StdErr;
    });

    private async Task ToolAction(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            _activityLogFile.TryAppend($"[App] Media tool action failed: {ex}");
            MessageBox.Show(ex.Message, "Media tool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void EnsureMedia() { if (!File.Exists(MediaPath.Text)) throw new InvalidOperationException("Select a valid media file."); }
    private void EnsureFfmpeg() { if (_ffmpeg is null) throw new InvalidOperationException("FFmpeg was not found."); }
    private void EnsureProbe() { EnsureMedia(); if (_ffprobe is null) throw new InvalidOperationException("ffprobe.exe was not found beside FFmpeg or in PATH."); }

    private void OpenPremiere_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PremiereHelper");
        if (!Directory.Exists(path)) path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "PremiereHelper"));
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        else MessageBox.Show("PremiereHelper folder not found. It is included at the package root.");
    }

    private static Process StartProcess(string exe, IEnumerable<string> args, bool redirectError)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = redirectError };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}.");
    }
    private static async Task<(int ExitCode, string StdOut, string StdErr)> CaptureAsync(string exe, IEnumerable<string> args, CancellationToken token)
    {
        using var p = StartProcess(exe, args, true);
        var stdout = p.StandardOutput.ReadToEndAsync(token); var stderr = p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token); return (p.ExitCode, await stdout, await stderr);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> CaptureLoggedAsync(string toolName, string exe, IEnumerable<string> args, CancellationToken token)
    {
        var argList = args as IReadOnlyList<string> ?? args.ToList();
        _activityLogFile.TryAppend($"[App] {toolName}: {FormatCommand(exe, argList)}");
        var result = await CaptureAsync(exe, argList, token);
        if (!string.IsNullOrWhiteSpace(result.StdOut)) _activityLogFile.TryAppend($"[{toolName}] stdout: {result.StdOut.Trim()}");
        if (!string.IsNullOrWhiteSpace(result.StdErr)) _activityLogFile.TryAppend($"[{toolName}] stderr: {result.StdErr.Trim()}");
        if (result.ExitCode != 0) _activityLogFile.TryAppend($"[App] {toolName} exited with code {result.ExitCode}.");
        return result;
    }
}
