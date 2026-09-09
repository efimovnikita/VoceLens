namespace VoceLens.Services.Audio;

public class NativeTtsLanguage
{
    public string Code { get; set; } = string.Empty; // e.g. "", "ru", "it", "en"
    public string DisplayName { get; set; } = string.Empty; // e.g. "Default (System Language)", "Russian (Russia)"

    public override string ToString() => DisplayName;
}

public class NativeTtsVoice
{
    public string Id { get; set; } = string.Empty; // voice id, or "" for Default Voice
    public string Name { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}

public interface INativeTtsService
{
    Task<IReadOnlyList<NativeTtsLanguage>> GetAvailableLanguagesAsync();
    Task<IReadOnlyList<NativeTtsVoice>> GetAvailableVoicesAsync(string? languageCode = null);
    Task SpeakAsync(string text, string? voiceIdOrLocale = null, CancellationToken cancellationToken = default);
    Task SpeakSentencesAsync(
        IReadOnlyList<string> sentences,
        int startSentenceIndex,
        string? voiceIdOrLocale,
        Action<int> onSentenceStarted,
        CancellationToken cancellationToken);
    void Stop();
}
