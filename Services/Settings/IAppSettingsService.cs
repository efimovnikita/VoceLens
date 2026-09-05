using VoceLens.Models;

namespace VoceLens.Services.Settings;

public interface IAppSettingsService
{
    string MistralApiKey { get; set; }
    string SelectedVoiceId { get; set; }
    string SelectedVoiceName { get; set; }
    string OcrModel { get; set; }
    string TtsModel { get; set; }
    int MaxChunkLength { get; set; }
    bool AutoPlay { get; set; }
    bool IsCropFrameEnabled { get; set; }
    CropFrameBounds CropFrame { get; set; }
    List<VoiceItem> CachedVoices { get; set; }

    void Save();
    void Load();
}
