using VoceLens.Services.Logging;

namespace VoceLens.Services.Audio;

public class NativeTtsService : INativeTtsService
{
    private CancellationTokenSource? _activeSpeechCts;
    private readonly object _lock = new();

    public async Task<IReadOnlyList<NativeTtsLanguage>> GetAvailableLanguagesAsync()
    {
        var languages = new List<NativeTtsLanguage>
        {
            new() { Code = "", DisplayName = "Default (System Language)" }
        };

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            if (locales != null)
            {
                var distinctLanguages = locales
                    .Where(l => !string.IsNullOrWhiteSpace(l.Language))
                    .GroupBy(l => l.Language.ToLowerInvariant())
                    .Select(g =>
                    {
                        var first = g.First();
                        string name = !string.IsNullOrWhiteSpace(first.Name) ? first.Name : g.Key.ToUpperInvariant();
                        return new NativeTtsLanguage
                        {
                            Code = g.Key,
                            DisplayName = name
                        };
                    })
                    .OrderBy(l => l.DisplayName)
                    .ToList();

                languages.AddRange(distinctLanguages);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to query native TTS locales: {ex.Message}", "NativeTTS");
        }

        return languages;
    }

    public async Task<IReadOnlyList<NativeTtsVoice>> GetAvailableVoicesAsync(string? languageCode = null)
    {
        var voices = new List<NativeTtsVoice>
        {
            new() { Id = "", Name = "Default Voice", DisplayName = "Default Voice" }
        };

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            if (locales != null)
            {
                var query = locales.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(languageCode))
                {
                    query = query.Where(l => l.Language.Equals(languageCode, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var loc in query)
                {
                    string displayName = loc.Name;
                    if (!string.IsNullOrWhiteSpace(loc.Id) && loc.Id != loc.Name)
                    {
                        displayName = $"{loc.Name} ({loc.Id})";
                    }

                    voices.Add(new NativeTtsVoice
                    {
                        Id = loc.Id,
                        Name = loc.Name,
                        LanguageCode = loc.Language,
                        CountryCode = loc.Country,
                        DisplayName = displayName
                    });
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to query native TTS voices: {ex.Message}", "NativeTTS");
        }

        return voices;
    }

    public async Task SpeakAsync(string text, string? voiceIdOrLocale = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        CancellationTokenSource linkedCts;
        lock (_lock)
        {
            _activeSpeechCts?.Cancel();
            _activeSpeechCts?.Dispose();
            _activeSpeechCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts = _activeSpeechCts;
        }

        try
        {
            SpeechOptions options = new()
            {
                Volume = 1.0f,
                Pitch = 1.0f
            };

            if (!string.IsNullOrWhiteSpace(voiceIdOrLocale))
            {
                try
                {
                    var locales = await TextToSpeech.Default.GetLocalesAsync();
                    var matchedLocale = locales?.FirstOrDefault(l => 
                        l.Id == voiceIdOrLocale || 
                        l.Language.Equals(voiceIdOrLocale, StringComparison.OrdinalIgnoreCase));

                    if (matchedLocale != null)
                    {
                        options.Locale = matchedLocale;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Could not apply voice '{voiceIdOrLocale}': {ex.Message}", "NativeTTS");
                }
            }

            await TextToSpeech.Default.SpeakAsync(text, options, linkedCts.Token);
        }
        finally
        {
            lock (_lock)
            {
                if (_activeSpeechCts == linkedCts)
                {
                    _activeSpeechCts.Dispose();
                    _activeSpeechCts = null;
                }
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try
            {
                _activeSpeechCts?.Cancel();
                _activeSpeechCts?.Dispose();
                _activeSpeechCts = null;
            }
            catch { }
        }
    }
}
