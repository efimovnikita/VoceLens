using System.Text.Json;
using VoceLens.Models;

namespace VoceLens.Services.Settings;

public class AppSettingsService : IAppSettingsService
{
    private const string KeyMistralApiKey = "mistral_api_key";
    private const string KeyVoiceId = "mistral_voice_id";
    private const string KeyVoiceName = "mistral_voice_name";
    private const string KeyOcrModel = "mistral_ocr_model";
    private const string KeyTtsModel = "mistral_tts_model";
    private const string KeyMaxChunkLength = "mistral_max_chunk_length";
    private const string KeyAutoPlay = "mistral_autoplay";
    private const string KeyCropFrameEnabled = "crop_frame_enabled";
    private const string KeyCropMarginTop = "crop_margin_top";
    private const string KeyCropMarginBottom = "crop_margin_bottom";
    private const string KeyCropMarginLeft = "crop_margin_left";
    private const string KeyCropMarginRight = "crop_margin_right";
    private const string KeyCachedVoices = "mistral_cached_voices";
    private const string KeyEnableAndroidTtsFallback = "enable_android_tts_fallback";
    private const string KeyFallbackLanguageCode = "fallback_language_code";
    private const string KeyFallbackLanguageName = "fallback_language_name";
    private const string KeyFallbackVoiceId = "fallback_voice_id";
    private const string KeyFallbackVoiceName = "fallback_voice_name";

    public string MistralApiKey { get; set; } = string.Empty;
    public string SelectedVoiceId { get; set; } = string.Empty;
    public string SelectedVoiceName { get; set; } = string.Empty;
    public string OcrModel { get; set; } = "mistral-ocr-latest";
    public string TtsModel { get; set; } = "voxtral-mini-tts-2603";
    public int MaxChunkLength { get; set; } = 1000;
    public bool AutoPlay { get; set; } = true;
    public bool IsCropFrameEnabled { get; set; } = true;
    public CropFrameBounds CropFrame { get; set; } = new(8, 8, 0, 0, true);
    public List<VoiceItem> CachedVoices { get; set; } = new();
    public bool EnableAndroidTtsFallback { get; set; } = true;
    public string FallbackLanguageCode { get; set; } = string.Empty;
    public string FallbackLanguageName { get; set; } = "Default (System Language)";
    public string FallbackVoiceId { get; set; } = string.Empty;
    public string FallbackVoiceName { get; set; } = "Default Voice";

    public AppSettingsService()
    {
        Load();
    }

    public void Load()
    {
        MistralApiKey = Preferences.Default.Get(KeyMistralApiKey, string.Empty);
        SelectedVoiceId = Preferences.Default.Get(KeyVoiceId, string.Empty);
        SelectedVoiceName = Preferences.Default.Get(KeyVoiceName, string.Empty);
        OcrModel = Preferences.Default.Get(KeyOcrModel, "mistral-ocr-latest");
        TtsModel = Preferences.Default.Get(KeyTtsModel, "voxtral-mini-tts-2603");
        MaxChunkLength = Preferences.Default.Get(KeyMaxChunkLength, 1000);
        if (MaxChunkLength <= 500)
        {
            MaxChunkLength = 1000;
        }
        AutoPlay = Preferences.Default.Get(KeyAutoPlay, true);
        IsCropFrameEnabled = Preferences.Default.Get(KeyCropFrameEnabled, true);
        EnableAndroidTtsFallback = Preferences.Default.Get(KeyEnableAndroidTtsFallback, true);
        FallbackLanguageCode = Preferences.Default.Get(KeyFallbackLanguageCode, string.Empty);
        FallbackLanguageName = Preferences.Default.Get(KeyFallbackLanguageName, "Default (System Language)");
        FallbackVoiceId = Preferences.Default.Get(KeyFallbackVoiceId, string.Empty);
        FallbackVoiceName = Preferences.Default.Get(KeyFallbackVoiceName, "Default Voice");

        int top = Preferences.Default.Get(KeyCropMarginTop, 8);
        int bottom = Preferences.Default.Get(KeyCropMarginBottom, 8);
        int left = Preferences.Default.Get(KeyCropMarginLeft, 0);
        int right = Preferences.Default.Get(KeyCropMarginRight, 0);
        CropFrame = new CropFrameBounds(top, bottom, left, right, IsCropFrameEnabled);

        string voicesJson = Preferences.Default.Get(KeyCachedVoices, string.Empty);
        if (!string.IsNullOrWhiteSpace(voicesJson))
        {
            try
            {
                CachedVoices = JsonSerializer.Deserialize<List<VoiceItem>>(voicesJson) ?? new List<VoiceItem>();
            }
            catch
            {
                CachedVoices = new List<VoiceItem>();
            }
        }
    }

    public void Save()
    {
        Preferences.Default.Set(KeyMistralApiKey, MistralApiKey);
        Preferences.Default.Set(KeyVoiceId, SelectedVoiceId);
        Preferences.Default.Set(KeyVoiceName, SelectedVoiceName);
        Preferences.Default.Set(KeyOcrModel, OcrModel);
        Preferences.Default.Set(KeyTtsModel, TtsModel);
        Preferences.Default.Set(KeyMaxChunkLength, MaxChunkLength);
        Preferences.Default.Set(KeyAutoPlay, AutoPlay);
        Preferences.Default.Set(KeyCropFrameEnabled, IsCropFrameEnabled);
        Preferences.Default.Set(KeyEnableAndroidTtsFallback, EnableAndroidTtsFallback);
        Preferences.Default.Set(KeyFallbackLanguageCode, FallbackLanguageCode);
        Preferences.Default.Set(KeyFallbackLanguageName, FallbackLanguageName);
        Preferences.Default.Set(KeyFallbackVoiceId, FallbackVoiceId);
        Preferences.Default.Set(KeyFallbackVoiceName, FallbackVoiceName);

        if (CropFrame != null)
        {
            Preferences.Default.Set(KeyCropMarginTop, CropFrame.TopPercent);
            Preferences.Default.Set(KeyCropMarginBottom, CropFrame.BottomPercent);
            Preferences.Default.Set(KeyCropMarginLeft, CropFrame.LeftPercent);
            Preferences.Default.Set(KeyCropMarginRight, CropFrame.RightPercent);
        }

        try
        {
            string voicesJson = JsonSerializer.Serialize(CachedVoices);
            Preferences.Default.Set(KeyCachedVoices, voicesJson);
        }
        catch
        {
            // Ignore serialization error
        }
    }
}
