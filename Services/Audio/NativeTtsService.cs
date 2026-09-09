#if ANDROID
using Android.Content;
using Android.OS;
using Android.Speech.Tts;
using Java.Util;
using AndroidTts = Android.Speech.Tts.TextToSpeech;
#endif
using VoceLens.Services.Logging;

namespace VoceLens.Services.Audio;

public class NativeTtsService : INativeTtsService, IDisposable
{
#if ANDROID
    private AndroidTts? _tts;
    private TtsInitListener? _initListener;
    private UtteranceProgressForwarder? _progressForwarder;
    private readonly TaskCompletionSource<bool> _initTcs = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    private TaskCompletionSource<bool>? _currentSpeechTcs;
    private Action<int>? _activeSentenceStartedCallback;
    private string? _expectedFinalUtteranceId;
    private readonly object _lock = new();
    private string? _appliedVoiceOrLocale;

    public NativeTtsService()
    {
        // Initialization will happen lazily on MainThread in EnsureInitializedAsync
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized && _tts != null)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized && _tts != null)
                return;

            if (_tts == null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        var context = Android.App.Application.Context;
                        _initListener = new TtsInitListener(status => OnInit(status));
                        _tts = new AndroidTts(context, _initListener);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Failed to instantiate Android TextToSpeech", ex, "NativeTTS");
                        _initTcs.TrySetException(ex);
                    }
                });
            }

            await _initTcs.Task;
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void OnInit(OperationResult status)
    {
        if (status == OperationResult.Success)
        {
            AppLog.Info("Android TextToSpeech engine initialized successfully.", "NativeTTS");
            try
            {
                _progressForwarder = new UtteranceProgressForwarder(
                    onStart: utteranceId => OnUtteranceStarted(utteranceId),
                    onDone: utteranceId => OnUtteranceFinished(utteranceId),
                    onError: utteranceId => OnUtteranceFinished(utteranceId)
                );
                _tts?.SetOnUtteranceProgressListener(_progressForwarder);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Failed to set UtteranceProgressListener: {ex.Message}", "NativeTTS");
            }
            _initTcs.TrySetResult(true);
        }
        else
        {
            AppLog.Error($"Android TextToSpeech OnInit failed with status: {status}", null, "NativeTTS");
            _initTcs.TrySetResult(false);
        }
    }

    private void OnUtteranceStarted(string utteranceId)
    {
        if (utteranceId.StartsWith("sent_") && int.TryParse(utteranceId.Substring(5), out int idx))
        {
            _activeSentenceStartedCallback?.Invoke(idx);
        }
    }

    private void OnUtteranceFinished(string utteranceId)
    {
        lock (_lock)
        {
            if (_expectedFinalUtteranceId != null && utteranceId == _expectedFinalUtteranceId)
            {
                _expectedFinalUtteranceId = null;
                _currentSpeechTcs?.TrySetResult(true);
            }
        }
    }

    private void ApplyVoiceOrLocale(string? voiceIdOrLocale)
    {
        if (_tts == null) return;

        if (string.IsNullOrWhiteSpace(voiceIdOrLocale))
        {
            try
            {
                _tts.SetLanguage(Java.Util.Locale.Default);
            }
            catch { }
            _appliedVoiceOrLocale = "";
            return;
        }

        if (_appliedVoiceOrLocale == voiceIdOrLocale)
            return;

        try
        {
            var voices = _tts.Voices;
            if (voices != null)
            {
                var matchedVoice = voices.FirstOrDefault(v =>
                    v.Name.Equals(voiceIdOrLocale, StringComparison.OrdinalIgnoreCase));

                if (matchedVoice != null)
                {
                    _tts.SetVoice(matchedVoice);
                    _appliedVoiceOrLocale = voiceIdOrLocale;
                    AppLog.Info($"Native TTS applied voice: {matchedVoice.Name} ({matchedVoice.Locale})", "NativeTTS");
                    return;
                }
            }

            var locale = Java.Util.Locale.ForLanguageTag(voiceIdOrLocale);
            if (locale != null)
            {
                var result = _tts.SetLanguage(locale);
                if (result != LanguageAvailableResult.MissingData && result != LanguageAvailableResult.NotSupported)
                {
                    _appliedVoiceOrLocale = voiceIdOrLocale;
                    AppLog.Info($"Native TTS applied language: {locale}", "NativeTTS");
                    return;
                }
            }

            var simpleLocale = new Java.Util.Locale(voiceIdOrLocale);
            _tts.SetLanguage(simpleLocale);
            _appliedVoiceOrLocale = voiceIdOrLocale;
            AppLog.Info($"Native TTS applied fallback locale: {simpleLocale}", "NativeTTS");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to apply voice/locale '{voiceIdOrLocale}': {ex.Message}", "NativeTTS");
        }
    }

    public async Task<IReadOnlyList<NativeTtsLanguage>> GetAvailableLanguagesAsync()
    {
        var languages = new List<NativeTtsLanguage>
        {
            new() { Code = "", DisplayName = "Default (System Language)" }
        };

        try
        {
            await EnsureInitializedAsync();
            var voices = _tts?.Voices;
            if (voices != null)
            {
                var distinctLanguages = voices
                    .Where(v => v.Locale != null && !string.IsNullOrWhiteSpace(v.Locale.Language))
                    .GroupBy(v => v.Locale.Language.ToLowerInvariant())
                    .Select(g =>
                    {
                        var first = g.First();
                        string name = !string.IsNullOrWhiteSpace(first.Locale.DisplayLanguage)
                            ? first.Locale.DisplayLanguage
                            : g.Key.ToUpperInvariant();
                        return new NativeTtsLanguage
                        {
                            Code = g.Key,
                            DisplayName = char.ToUpperInvariant(name[0]) + name.Substring(1)
                        };
                    })
                    .OrderBy(l => l.DisplayName)
                    .ToList();

                languages.AddRange(distinctLanguages);
                return languages;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to query native TTS languages from Android engine: {ex.Message}", "NativeTTS");
        }

        return await GetAvailableLanguagesMauiAsync();
    }

    public async Task<IReadOnlyList<NativeTtsVoice>> GetAvailableVoicesAsync(string? languageCode = null)
    {
        var voiceList = new List<NativeTtsVoice>
        {
            new() { Id = "", Name = "Default Voice", DisplayName = "Default Voice" }
        };

        try
        {
            await EnsureInitializedAsync();
            var voices = _tts?.Voices;
            if (voices != null)
            {
                var query = voices.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(languageCode))
                {
                    query = query.Where(v => v.Locale != null &&
                        (v.Locale.Language.Equals(languageCode, StringComparison.OrdinalIgnoreCase) ||
                         v.Locale.ToLanguageTag().Equals(languageCode, StringComparison.OrdinalIgnoreCase)));
                }

                foreach (var v in query.OrderBy(v => v.Name))
                {
                    string displayName = v.Name;
                    if (v.Locale != null && !string.IsNullOrWhiteSpace(v.Locale.DisplayName))
                    {
                        displayName = $"{v.Locale.DisplayName} - {v.Name}";
                    }

                    voiceList.Add(new NativeTtsVoice
                    {
                        Id = v.Name,
                        Name = v.Name,
                        LanguageCode = v.Locale?.Language ?? "",
                        CountryCode = v.Locale?.Country ?? "",
                        DisplayName = displayName
                    });
                }

                return voiceList;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to query native TTS voices from Android engine: {ex.Message}", "NativeTTS");
        }

        return await GetAvailableVoicesMauiAsync(languageCode);
    }

    public Task SpeakAsync(string text, string? voiceIdOrLocale = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        return SpeakSentencesAsync(new[] { text }, 0, voiceIdOrLocale, _ => {}, cancellationToken);
    }

    public async Task SpeakSentencesAsync(
        IReadOnlyList<string> sentences,
        int startSentenceIndex,
        string? voiceIdOrLocale,
        Action<int> onSentenceStarted,
        CancellationToken cancellationToken)
    {
        if (sentences == null || sentences.Count == 0)
            return;

        var validSentences = new List<(int OriginalIndex, string Text)>();
        for (int i = startSentenceIndex; i < sentences.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(sentences[i]))
            {
                validSentences.Add((i, sentences[i].Trim()));
            }
        }

        if (validSentences.Count == 0)
            return;

        await EnsureInitializedAsync();
        if (_tts == null)
        {
            AppLog.Error("Android TextToSpeech is not available.", null, "NativeTTS");
            return;
        }

        ApplyVoiceOrLocale(voiceIdOrLocale);

        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            _currentSpeechTcs?.TrySetResult(false);
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _currentSpeechTcs = tcs;
            _activeSentenceStartedCallback = onSentenceStarted;
            _expectedFinalUtteranceId = $"sent_{validSentences[^1].OriginalIndex}";
        }

        using var reg = cancellationToken.Register(() =>
        {
            Stop();
        });

        try
        {
            for (int i = 0; i < validSentences.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var (origIdx, sentenceText) = validSentences[i];
                string utteranceId = $"sent_{origIdx}";
                var mode = (i == 0) ? QueueMode.Flush : QueueMode.Add;

                var bundle = new Bundle();
                _tts.Speak(sentenceText, mode, bundle, utteranceId);
            }

            await tcs.Task;
        }
        finally
        {
            lock (_lock)
            {
                if (_currentSpeechTcs == tcs)
                {
                    _currentSpeechTcs = null;
                    _activeSentenceStartedCallback = null;
                    _expectedFinalUtteranceId = null;
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
                _tts?.Stop();
            }
            catch { }

            _activeSentenceStartedCallback = null;
            _expectedFinalUtteranceId = null;
            _currentSpeechTcs?.TrySetResult(false);
            _currentSpeechTcs = null;
        }
    }

    public void Dispose()
    {
        try
        {
            _tts?.Stop();
            _tts?.Shutdown();
            _tts?.Dispose();
            _tts = null;
        }
        catch { }
    }

#else
    // Non-Android fallback
    private CancellationTokenSource? _activeSpeechCts;
    private readonly object _lock = new();

    public async Task<IReadOnlyList<NativeTtsLanguage>> GetAvailableLanguagesAsync()
    {
        return await GetAvailableLanguagesMauiAsync();
    }

    public async Task<IReadOnlyList<NativeTtsVoice>> GetAvailableVoicesAsync(string? languageCode = null)
    {
        return await GetAvailableVoicesMauiAsync(languageCode);
    }

    public async Task SpeakAsync(string text, string? voiceIdOrLocale = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await TextToSpeech.Default.SpeakAsync(text, cancellationToken: cancellationToken);
    }

    public async Task SpeakSentencesAsync(
        IReadOnlyList<string> sentences,
        int startSentenceIndex,
        string? voiceIdOrLocale,
        Action<int> onSentenceStarted,
        CancellationToken cancellationToken)
    {
        for (int i = startSentenceIndex; i < sentences.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            string s = sentences[i];
            if (string.IsNullOrWhiteSpace(s)) continue;
            onSentenceStarted(i);
            await SpeakAsync(s, voiceIdOrLocale, cancellationToken);
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

    public void Dispose()
    {
    }
#endif

    // Shared MAUI fallback queries
    private static async Task<IReadOnlyList<NativeTtsLanguage>> GetAvailableLanguagesMauiAsync()
    {
        var languages = new List<NativeTtsLanguage>
        {
            new() { Code = "", DisplayName = "Default (System Language)" }
        };

        try
        {
            var locales = await Microsoft.Maui.Media.TextToSpeech.Default.GetLocalesAsync();
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
            AppLog.Warn($"Failed to query MAUI TTS locales: {ex.Message}", "NativeTTS");
        }

        return languages;
    }

    private static async Task<IReadOnlyList<NativeTtsVoice>> GetAvailableVoicesMauiAsync(string? languageCode)
    {
        var voices = new List<NativeTtsVoice>
        {
            new() { Id = "", Name = "Default Voice", DisplayName = "Default Voice" }
        };

        try
        {
            var locales = await Microsoft.Maui.Media.TextToSpeech.Default.GetLocalesAsync();
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
            AppLog.Warn($"Failed to query MAUI TTS voices: {ex.Message}", "NativeTTS");
        }

        return voices;
    }
}

#if ANDROID
internal class TtsInitListener : Java.Lang.Object, AndroidTts.IOnInitListener
{
    private readonly Action<OperationResult> _onInit;

    public TtsInitListener(Action<OperationResult> onInit)
    {
        _onInit = onInit;
    }

    public void OnInit(OperationResult status)
    {
        _onInit(status);
    }
}

internal class UtteranceProgressForwarder : UtteranceProgressListener
{
    private readonly Action<string> _onStart;
    private readonly Action<string> _onDone;
    private readonly Action<string> _onError;

    public UtteranceProgressForwarder(Action<string> onStart, Action<string> onDone, Action<string> onError)
    {
        _onStart = onStart;
        _onDone = onDone;
        _onError = onError;
    }

    public override void OnStart(string? utteranceId)
    {
        if (!string.IsNullOrEmpty(utteranceId))
        {
            _onStart(utteranceId);
        }
    }

    public override void OnDone(string? utteranceId)
    {
        if (!string.IsNullOrEmpty(utteranceId))
        {
            _onDone(utteranceId);
        }
    }

    public override void OnError(string? utteranceId)
    {
        if (!string.IsNullOrEmpty(utteranceId))
        {
            _onError(utteranceId);
        }
    }

    public override void OnStop(string? utteranceId, bool interrupted)
    {
        if (!string.IsNullOrEmpty(utteranceId))
        {
            _onDone(utteranceId);
        }
    }
}
#endif
