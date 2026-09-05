using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VoceLens.Models;
using VoceLens.Services.Audio;
using VoceLens.Services.Mistral;
using VoceLens.Services.Logging;
using VoceLens.Services.Overlay;
using VoceLens.Services.Settings;
using VoceLens.Services.Workflow;
#if ANDROID
using VoceLens.Platforms.Android.Overlay;
#endif

namespace VoceLens.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IAppSettingsService _settings;
    private readonly IMistralClient _mistralClient;
    private readonly IFloatingOverlayService _overlayService;
    private readonly IAudioPlaybackManager _playbackManager;
    private readonly IOcrSpeechWorkflowController _workflow;
    private readonly IAppLogService _logService;

    private string _apiKey = string.Empty;
    private VoiceItem? _selectedVoice;
    private string _statusMessage = "Ready";
    private string _extractedText = string.Empty;
    private bool _isBusy;
    private bool _isLoadingVoices;
    private bool _isOverlayRunning;
    private bool _canDrawOverlays;
    private bool _hasCapturePermission;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VoiceItem> Voices { get; } = new();

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (_apiKey != value)
            {
                _apiKey = value;
                _settings.MistralApiKey = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }
    }

    public VoiceItem? SelectedVoice
    {
        get => _selectedVoice;
        set
        {
            if (_selectedVoice != value)
            {
                _selectedVoice = value;
                if (value != null)
                {
                    _settings.SelectedVoiceId = value.Id;
                    _settings.SelectedVoiceName = value.Name;
                    _settings.Save();
                }
                OnPropertyChanged();
            }
        }
    }

    public string OcrModel
    {
        get => _settings.OcrModel;
        set
        {
            _settings.OcrModel = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public string TtsModel
    {
        get => _settings.TtsModel;
        set
        {
            _settings.TtsModel = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public int MaxChunkLength
    {
        get => _settings.MaxChunkLength;
        set
        {
            _settings.MaxChunkLength = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool AutoPlay
    {
        get => _settings.AutoPlay;
        set
        {
            _settings.AutoPlay = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string ExtractedText
    {
        get => _extractedText;
        set { _extractedText = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public bool IsLoadingVoices
    {
        get => _isLoadingVoices;
        set 
        { 
            _isLoadingVoices = value; 
            OnPropertyChanged();
            OnPropertyChanged(nameof(FetchVoicesButtonText));
        }
    }

    public string FetchVoicesButtonText => IsLoadingVoices ? "⏳ Fetching Voices from Mistral..." : "🔄 Fetch Mistral Voices";

    public bool IsOverlayRunning
    {
        get => _isOverlayRunning;
        set
        {
            if (_isOverlayRunning != value)
            {
                _isOverlayRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OverlayButtonText));
                OnPropertyChanged(nameof(OverlayButtonColor));
            }
        }
    }

    public string OverlayButtonText => IsOverlayRunning ? "⏹ Stop Floating Button" : "▶ Start Floating Button";
    public string OverlayButtonColor => IsOverlayRunning ? "#EF4444" : "#10B981";

    public bool IsCropEnabled
    {
        get => _settings.IsCropFrameEnabled;
        set
        {
            if (_settings.IsCropFrameEnabled != value)
            {
                _settings.IsCropFrameEnabled = value;
                _settings.Save();
                OnPropertyChanged();
                NotifyCropChanges();
            }
        }
    }

    public int CropTopPercent
    {
        get => _settings.CropFrame?.TopPercent ?? 8;
        set
        {
            if (_settings.CropFrame != null && _settings.CropFrame.TopPercent != value)
            {
                _settings.CropFrame.TopPercent = value;
                _settings.Save();
                OnPropertyChanged();
                NotifyCropChanges();
            }
        }
    }

    public int CropBottomPercent
    {
        get => _settings.CropFrame?.BottomPercent ?? 8;
        set
        {
            if (_settings.CropFrame != null && _settings.CropFrame.BottomPercent != value)
            {
                _settings.CropFrame.BottomPercent = value;
                _settings.Save();
                OnPropertyChanged();
                NotifyCropChanges();
            }
        }
    }

    public int CropLeftPercent
    {
        get => _settings.CropFrame?.LeftPercent ?? 0;
        set
        {
            if (_settings.CropFrame != null && _settings.CropFrame.LeftPercent != value)
            {
                _settings.CropFrame.LeftPercent = value;
                _settings.Save();
                OnPropertyChanged();
                NotifyCropChanges();
            }
        }
    }

    public int CropRightPercent
    {
        get => _settings.CropFrame?.RightPercent ?? 0;
        set
        {
            if (_settings.CropFrame != null && _settings.CropFrame.RightPercent != value)
            {
                _settings.CropFrame.RightPercent = value;
                _settings.Save();
                OnPropertyChanged();
                NotifyCropChanges();
            }
        }
    }

    public string CropSummaryText => IsCropEnabled
        ? $"✂️ Top: {CropTopPercent}% | Bottom: {CropBottomPercent}% | Left: {CropLeftPercent}% | Right: {CropRightPercent}%"
        : "Full Screen (No Edge Crop)";

    private ImageSource? _calibrationScreenshot;
    public ImageSource? CalibrationScreenshot
    {
        get => _calibrationScreenshot;
        set
        {
            _calibrationScreenshot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCalibrationScreenshot));
            OnPropertyChanged(nameof(ShowPlaceholderMockup));
        }
    }

    public bool HasCalibrationScreenshot => CalibrationScreenshot != null;
    public bool ShowPlaceholderMockup => !HasCalibrationScreenshot;

    public const double PreviewPhoneWidth = 180.0;
    public const double PreviewPhoneHeight = 360.0;

    public Thickness PreviewCropMargin => new Thickness(
        PreviewPhoneWidth * (CropLeftPercent / 100.0),
        PreviewPhoneHeight * (CropTopPercent / 100.0),
        PreviewPhoneWidth * (CropRightPercent / 100.0),
        PreviewPhoneHeight * (CropBottomPercent / 100.0)
    );

    public double PreviewTopMarginHeight => PreviewPhoneHeight * (CropTopPercent / 100.0);
    public double PreviewBottomMarginHeight => PreviewPhoneHeight * (CropBottomPercent / 100.0);
    public double PreviewLeftMarginWidth => PreviewPhoneWidth * (CropLeftPercent / 100.0);
    public double PreviewRightMarginWidth => PreviewPhoneWidth * (CropRightPercent / 100.0);

    public int PreviewActiveWidthPercent => Math.Max(0, 100 - CropLeftPercent - CropRightPercent);
    public int PreviewActiveHeightPercent => Math.Max(0, 100 - CropTopPercent - CropBottomPercent);

    public string PreviewCropInfoText => IsCropEnabled
        ? $"📐 Active Capture: {PreviewActiveWidthPercent}% W × {PreviewActiveHeightPercent}% H"
        : "📐 Active Capture: 100% W × 100% H (Full Screen)";

    public bool CanStopPlayback => _workflow.CurrentState == AppProcessingState.Playing || _workflow.CurrentState == AppProcessingState.Paused;

    private void NotifyCropChanges()
    {
        OnPropertyChanged(nameof(CropSummaryText));
        OnPropertyChanged(nameof(PreviewCropMargin));
        OnPropertyChanged(nameof(PreviewTopMarginHeight));
        OnPropertyChanged(nameof(PreviewBottomMarginHeight));
        OnPropertyChanged(nameof(PreviewLeftMarginWidth));
        OnPropertyChanged(nameof(PreviewRightMarginWidth));
        OnPropertyChanged(nameof(PreviewActiveWidthPercent));
        OnPropertyChanged(nameof(PreviewActiveHeightPercent));
        OnPropertyChanged(nameof(PreviewCropInfoText));
    }

    public bool CanDrawOverlays
    {
        get => _canDrawOverlays;
        set
        {
            if (_canDrawOverlays != value)
            {
                _canDrawOverlays = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDrawOverlaysStatus));
                OnPropertyChanged(nameof(IsOverlayPermissionMissing));
            }
        }
    }

    public bool IsOverlayPermissionMissing => !CanDrawOverlays;
    public string CanDrawOverlaysStatus => CanDrawOverlays ? "✅ Granted" : "⚠️ NOT GRANTED (Tap 'Grant')";

    public bool HasCapturePermission
    {
        get => _hasCapturePermission;
        set
        {
            if (_hasCapturePermission != value)
            {
                _hasCapturePermission = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCapturePermissionStatus));
            }
        }
    }

    public string HasCapturePermissionStatus => HasCapturePermission ? "✅ Authorized" : "⚠️ NOT AUTHORIZED (Tap 'Authorize')";

    public ICommand FetchVoicesCommand { get; }
    public ICommand ToggleOverlayCommand { get; }
    public ICommand ResetCropMarginsCommand { get; }
    public ICommand RequestOverlayPermissionCommand { get; }
    public ICommand RequestCapturePermissionCommand { get; }
    public ICommand OpenXiaomiPermissionsCommand { get; }
    public ICommand TestScreenCaptureOcrCommand { get; }
    public ICommand PlayAudioCommand { get; }
    public ICommand PauseAudioCommand { get; }
    public ICommand StopAudioCommand { get; }
    public ICommand StopPlaybackAndClearCommand { get; }
    public ICommand CaptureTargetAppCalibrationCommand { get; }
    public ICommand LoadLastScreenshotCalibrationCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand ClearLogsCommand { get; }

    public string FormattedLogs => _logService.GetFormattedLogText(newestFirst: true);
    public string LogCountText => $"({_logService.GetEntries().Count}/500)";

    public MainViewModel(
        IAppSettingsService settings,
        IMistralClient mistralClient,
        IFloatingOverlayService overlayService,
        IAudioPlaybackManager playbackManager,
        IOcrSpeechWorkflowController workflow,
        IAppLogService logService)
    {
        _settings = settings;
        _mistralClient = mistralClient;
        _overlayService = overlayService;
        _playbackManager = playbackManager;
        _workflow = workflow;
        _logService = logService;

        _apiKey = _settings.MistralApiKey;

        FetchVoicesCommand = new Command(async () => await FetchVoicesAsync());
        ToggleOverlayCommand = new Command(ToggleOverlay);
        ResetCropMarginsCommand = new Command(ResetCropMargins);
        RequestOverlayPermissionCommand = new Command(RequestOverlayPermission);
        RequestCapturePermissionCommand = new Command(RequestCapturePermission);
        OpenXiaomiPermissionsCommand = new Command(OpenXiaomiPermissions);
        TestScreenCaptureOcrCommand = new Command(async () => await TestScreenCaptureAndReadAsync());
        PlayAudioCommand = new Command(() => _playbackManager.Resume());
        PauseAudioCommand = new Command(() => _playbackManager.Pause());
        StopAudioCommand = new Command(() => _playbackManager.Stop());
        StopPlaybackAndClearCommand = new Command(StopPlaybackAndClear);
        CaptureTargetAppCalibrationCommand = new Command(async () => await CaptureTargetAppCalibrationAsync());
        LoadLastScreenshotCalibrationCommand = new Command(LoadLastScreenshotCalibration);
        CopyLogsCommand = new Command(async () => await CopyLogsAsync());
        ClearLogsCommand = new Command(ClearLogs);

        _logService.LogsChanged += (s, e) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(FormattedLogs));
                OnPropertyChanged(nameof(LogCountText));
            });
        };

        _workflow.StatusChanged += (s, e) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = e.Message;
                OnPropertyChanged(nameof(CanStopPlayback));
                if (!string.IsNullOrEmpty(_workflow.LastExtractedText))
                {
                    ExtractedText = _workflow.LastExtractedText;
                }
            });
        };

        _overlayService.ServiceStateChanged += (running) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOverlayRunning = running;
            });
        };

        RefreshPermissionsAndState();
        LoadCachedVoices();
    }

    public void RefreshPermissionsAndState()
    {
        CanDrawOverlays = _overlayService.CanDrawOverlays();
        HasCapturePermission = _overlayService.HasScreenCapturePermission;
        IsOverlayRunning = _overlayService.IsServiceRunning;
        OnPropertyChanged(nameof(FormattedLogs));
        OnPropertyChanged(nameof(LogCountText));

        if (!CanDrawOverlays)
        {
            StatusMessage = "⚠️ Permission needed: 'Display over other apps' is not granted. Please tap 'Grant' below.";
        }
    }

    private void LoadCachedVoices()
    {
        Voices.Clear();
        foreach (var voice in _settings.CachedVoices)
        {
            Voices.Add(voice);
        }

        if (!string.IsNullOrEmpty(_settings.SelectedVoiceId))
        {
            SelectedVoice = Voices.FirstOrDefault(v => v.Id == _settings.SelectedVoiceId);
        }
    }

    private async Task FetchVoicesAsync()
    {
        string keyToUse = ApiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyToUse))
        {
            StatusMessage = "⚠️ Please enter your Mistral API Key first.";
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Mistral API Key Missing", "Please enter your Mistral API key in the field above.", "OK");
            }
            return;
        }

        try
        {
            IsLoadingVoices = true;
            StatusMessage = "Connecting to Mistral voices API...";

            var voices = await _mistralClient.GetVoicesAsync(keyToUse);

            Voices.Clear();
            foreach (var voice in voices)
            {
                Voices.Add(voice);
            }

            _settings.CachedVoices = voices;
            _settings.Save();

            if (voices.Count > 0)
            {
                bool hasCustom = voices.Any(v => !string.IsNullOrEmpty(v.UserId));
                SelectedVoice = voices.FirstOrDefault(v => v.Id == _settings.SelectedVoiceId) ?? voices[0];
                StatusMessage = hasCustom
                    ? $"✅ Loaded {voices.Count} voice(s) (including custom user voices)."
                    : $"✅ Loaded {voices.Count} standard Mistral voice(s).";
            }
            else
            {
                StatusMessage = "⚠️ Mistral returned 0 voices for this account.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to load voices: {ex.Message}";
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error Fetching Voices", ex.Message, "OK");
            }
        }
        finally
        {
            IsLoadingVoices = false;
        }
    }

    private void ToggleOverlay()
    {
        if (!_overlayService.CanDrawOverlays())
        {
            StatusMessage = "⚠️ Permission required: In the Android Settings page that just opened, turn ON 'Allow display over other apps' for VoceLens, then return here and tap 'Start Floating Button' again.";
            _overlayService.RequestOverlayPermission();
            return;
        }

        try
        {
            if (IsOverlayRunning)
            {
                _overlayService.StopFloatingButton();
                IsOverlayRunning = false;
                StatusMessage = "Floating button stopped.";
            }
            else
            {
                _overlayService.StartFloatingButton();
                IsOverlayRunning = true;
                StatusMessage = "✅ Floating button is now active on your screen (🎙️ on the left edge)! Switch to any app to use it.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error starting floating button: {ex.Message}";
        }
    }

    private void ResetCropMargins()
    {
        CropTopPercent = 8;
        CropBottomPercent = 8;
        CropLeftPercent = 0;
        CropRightPercent = 0;
        IsCropEnabled = true;
        StatusMessage = "Screen crop margins reset to default (Top: 8%, Bottom: 8%, Sides: 0%).";
    }

    private void RequestOverlayPermission()
    {
        _overlayService.RequestOverlayPermission();
    }

    private void RequestCapturePermission()
    {
        _overlayService.RequestScreenCapturePermission();
    }

    private void OpenXiaomiPermissions()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            OverlayPermissionHelper.OpenMiuiPermissionActivity(activity);
        }
#endif
    }

    private async Task TestScreenCaptureAndReadAsync()
    {
        try
        {
            IsBusy = true;
            await _workflow.ProcessScreenCaptureAndReadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StopPlaybackAndClear()
    {
        _workflow.StopPlaybackAndClear();
        StatusMessage = "⏹ Playback stopped and queue cleared.";
        OnPropertyChanged(nameof(CanStopPlayback));
    }

    private async Task CaptureTargetAppCalibrationAsync()
    {
        try
        {
            StatusMessage = "📱 Minimizing VoceLens... Open your reader! Snapping in 2.5s...";

#if ANDROID
            Platform.CurrentActivity?.MoveTaskToBack(true);
#endif
            await Task.Delay(2500);

            byte[] fullDisplayBytes = await _overlayService.CaptureFullDisplayAsync();
            if (fullDisplayBytes != null && fullDisplayBytes.Length > 0)
            {
                SetCalibrationImage(fullDisplayBytes);
                StatusMessage = "✅ Reader snapshot captured! Adjust crop margins over your book text.";
            }

#if ANDROID
            var context = Android.App.Application.Context;
            var intent = new Android.Content.Intent(context, typeof(MainActivity));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ReorderToFront);
            context.StartActivity(intent);
#endif
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to capture screenshot: {ex.Message}";
        }
    }

    private void LoadLastScreenshotCalibration()
    {
        byte[]? bytes = _overlayService.LastCapturedFullDisplayJpeg;
        if (bytes != null && bytes.Length > 0)
        {
            SetCalibrationImage(bytes);
            StatusMessage = "✅ Loaded last screenshot into preview!";
        }
        else
        {
            StatusMessage = "ℹ️ No screenshot taken yet. Tap the floating button or '📸 Reader Screenshot'!";
        }
    }

    private void SetCalibrationImage(byte[] bytes)
    {
        CalibrationScreenshot = ImageSource.FromStream(() => new MemoryStream(bytes));
        NotifyCropChanges();
    }

    private async Task CopyLogsAsync()
    {
        try
        {
            string text = FormattedLogs;
            await Clipboard.Default.SetTextAsync(text);
            StatusMessage = "📋 Logs copied to clipboard!";
            AppLog.Info("Logs copied to clipboard by user.", "UI");
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to copy logs: {ex.Message}";
            AppLog.Error("Failed to copy logs to clipboard", ex, "UI");
        }
    }

    private void ClearLogs()
    {
        _logService.Clear();
        StatusMessage = "🗑️ Application log cleared.";
        AppLog.Info("Log buffer cleared by user.", "UI");
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
