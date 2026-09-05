using VoceLens.Models;
using VoceLens.Services.Audio;
using VoceLens.Services.Logging;
using VoceLens.Services.Mistral;
using VoceLens.Services.Overlay;
using VoceLens.Services.Settings;

namespace VoceLens.Services.Workflow;

public class OcrSpeechWorkflowController : IOcrSpeechWorkflowController
{
    private readonly IMistralClient _mistralClient;
    private readonly IAppSettingsService _settingsService;
    private readonly IAudioPlaybackManager _audioPlaybackManager;
    private readonly IFloatingOverlayService _overlayService;

    private AppProcessingState _currentState = AppProcessingState.Idle;
    private string _currentStatusMessage = "Ready";
    private string _lastExtractedText = string.Empty;

    public AppProcessingState CurrentState => _currentState;
    public string CurrentStatusMessage => _currentStatusMessage;
    public string LastExtractedText => _lastExtractedText;

    public event EventHandler<ProcessingStatusChangedEventArgs>? StatusChanged;

    public OcrSpeechWorkflowController(
        IMistralClient mistralClient,
        IAppSettingsService settingsService,
        IAudioPlaybackManager audioPlaybackManager,
        IFloatingOverlayService overlayService)
    {
        _mistralClient = mistralClient;
        _settingsService = settingsService;
        _audioPlaybackManager = audioPlaybackManager;
        _overlayService = overlayService;

        _audioPlaybackManager.StatusChanged += (s, e) =>
        {
            _currentState = e.State;
            _currentStatusMessage = e.Message;
            StatusChanged?.Invoke(this, e);
        };
    }

    public async Task ProcessScreenCaptureAndReadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settingsService.MistralApiKey))
        {
            AppLog.Error("Mistral API key is missing. Please set it in Settings.", null, "Workflow");
            UpdateState(AppProcessingState.Error, "Mistral API key is missing. Please set it in Settings.");
            return;
        }

        try
        {
            AppLog.Info($"Screen capture initiated (CropFrameEnabled={_settingsService.IsCropFrameEnabled})", "Workflow");
            UpdateState(AppProcessingState.CapturingScreen, "Capturing screen region...");
            byte[] screenshotBytes = await _overlayService.CaptureScreenAsync(_settingsService.IsCropFrameEnabled);

            if (screenshotBytes == null || screenshotBytes.Length == 0)
            {
                AppLog.Warn("Screenshot capture returned empty buffer or was cancelled.", "Workflow");
                UpdateState(AppProcessingState.Error, "Screenshot capture failed or was cancelled.");
                return;
            }

            AppLog.Info($"Screen captured ({screenshotBytes.Length / 1024} KB). Proceeding to OCR...", "Workflow");
            await ProcessImageBytesAndReadAsync(screenshotBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Capture Error: {ex.Message}", ex, "Workflow");
            UpdateState(AppProcessingState.Error, $"Capture Error: {ex.Message}");
        }
    }

    public async Task ProcessImageBytesAndReadAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settingsService.MistralApiKey))
        {
            AppLog.Error("Mistral API key is missing. Please set it in Settings.", null, "MistralOCR");
            UpdateState(AppProcessingState.Error, "Mistral API key is missing. Please set it in Settings.");
            return;
        }

        try
        {
            AppLog.Info($"Sending image ({imageBytes.Length / 1024} KB) to Mistral OCR API (model: {_settingsService.OcrModel})...", "MistralOCR");
            UpdateState(AppProcessingState.ScanningOcr, "Extracting text with Mistral OCR...");
            string extractedText = await _mistralClient.ExtractTextFromScreenshotAsync(
                _settingsService.MistralApiKey,
                imageBytes,
                _settingsService.OcrModel,
                cancellationToken);

            _lastExtractedText = extractedText?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_lastExtractedText))
            {
                AppLog.Warn("Mistral OCR completed: no text detected on the image.", "MistralOCR");
                UpdateState(AppProcessingState.Idle, "No text detected on the image.");
                return;
            }

            string preview = _lastExtractedText.Length > 80 ? _lastExtractedText.Substring(0, 80) + "..." : _lastExtractedText;
            AppLog.Info($"Mistral OCR success! Extracted {_lastExtractedText.Length} chars: \"{preview}\"", "MistralOCR");

            UpdateState(AppProcessingState.TextChunking, "Text extracted successfully. Preparing audio speech...");
            await _audioPlaybackManager.StartReadingTextAsync(_lastExtractedText, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Mistral OCR Error: {ex.Message}", ex, "MistralOCR");
            UpdateState(AppProcessingState.Error, $"OCR Error: {ex.Message}");
        }
    }

    public async Task ProcessCustomTextAndReadAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            UpdateState(AppProcessingState.Idle, "No text provided.");
            return;
        }

        _lastExtractedText = text.Trim();
        await _audioPlaybackManager.StartReadingTextAsync(_lastExtractedText, cancellationToken);
    }

    public void StopPlaybackAndClear()
    {
        _audioPlaybackManager.Stop();
        _lastExtractedText = string.Empty;
        UpdateState(AppProcessingState.Idle, "Ready");
    }

    private void UpdateState(AppProcessingState state, string message, int current = 0, int total = 0)
    {
        _currentState = state;
        _currentStatusMessage = message;
        StatusChanged?.Invoke(this, new ProcessingStatusChangedEventArgs(state, message, current, total));

        if (state == AppProcessingState.Error)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(3500);
                if (_currentState == AppProcessingState.Error)
                {
                    UpdateState(AppProcessingState.Idle, "Ready");
                }
            });
        }
    }
}
