using System.Security.Cryptography;
using System.Text;
using VoceLens.Models;
using VoceLens.Services.Chunking;
using VoceLens.Services.Logging;
using VoceLens.Services.Mistral;
using VoceLens.Services.Settings;

namespace VoceLens.Services.Audio;

public class AudioPlaybackManager : IAudioPlaybackManager
{
    private readonly IMistralClient _mistralClient;
    private readonly IAppSettingsService _settingsService;
    private readonly ITextChunker _textChunker;
    private readonly IPlatformAudioPlayer _audioPlayer;

    private readonly List<string> _chunks = new();
    private readonly Dictionary<int, string> _cachedAudioFiles = new();
    private int _currentIndex = -1;
    private AppProcessingState _currentState = AppProcessingState.Idle;
    private CancellationTokenSource? _cts;

    public IReadOnlyList<string> Chunks => _chunks;
    public int CurrentChunkIndex => _currentIndex;
    public AppProcessingState CurrentState => _currentState;

    public event EventHandler<ProcessingStatusChangedEventArgs>? StatusChanged;

    public AudioPlaybackManager(
        IMistralClient mistralClient,
        IAppSettingsService settingsService,
        ITextChunker textChunker,
        IPlatformAudioPlayer audioPlayer)
    {
        _mistralClient = mistralClient;
        _settingsService = settingsService;
        _textChunker = textChunker;
        _audioPlayer = audioPlayer;

        _audioPlayer.PlaybackEnded += OnPlaybackEnded;
    }

    public async Task StartReadingTextAsync(string fullText, CancellationToken cancellationToken = default)
    {
        Stop();

        _chunks.Clear();
        _cachedAudioFiles.Clear();
        _currentIndex = -1;

        fullText = TextSanitizer.SanitizeOcrTranscript(fullText);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            UpdateState(AppProcessingState.Idle, "No text provided.");
            return;
        }

        UpdateState(AppProcessingState.TextChunking, "Splitting text into chunks...");
        var split = _textChunker.SplitIntoChunks(fullText, _settingsService.MaxChunkLength);
        _chunks.AddRange(split);
        AppLog.Info($"Text split into {_chunks.Count} chunks (total {fullText.Length} chars, max: {_settingsService.MaxChunkLength})", "TTS");

        if (_chunks.Count == 0)
        {
            AppLog.Warn("No readable sentences found after chunking.", "TTS");
            UpdateState(AppProcessingState.Idle, "No readable sentences found.");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await PlayChunkAsync(0, _cts.Token);
    }

    public async Task PlayChunkAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0 || index >= _chunks.Count)
        {
            Stop();
            AppLog.Info("Completed playback of all text chunks.", "Audio");
            UpdateState(AppProcessingState.Idle, "Finished reading all parts.");
            return;
        }

        _currentIndex = index;
        string text = _chunks[_currentIndex];

        try
        {
            string? audioFilePath;

            if (_cachedAudioFiles.TryGetValue(_currentIndex, out var existingPath) && File.Exists(existingPath))
            {
                audioFilePath = existingPath;
                AppLog.Info($"Using preloaded audio cache for chunk {_currentIndex + 1}/{_chunks.Count}", "TTS");
            }
            else
            {
                UpdateState(AppProcessingState.GeneratingAudio, $"Generating speech for part {_currentIndex + 1}/{_chunks.Count}...", _currentIndex, _chunks.Count);
                AppLog.Info($"Requesting Mistral TTS for chunk {_currentIndex + 1}/{_chunks.Count} ({text.Length} chars, voice: {_settingsService.SelectedVoiceName ?? _settingsService.SelectedVoiceId})...", "MistralTTS");

                byte[] audioBytes = await _mistralClient.GenerateSpeechAsync(
                    _settingsService.MistralApiKey,
                    text,
                    _settingsService.SelectedVoiceId,
                    _settingsService.TtsModel,
                    cancellationToken);

                audioFilePath = SaveAudioToCache(_currentIndex, audioBytes);
                _cachedAudioFiles[_currentIndex] = audioFilePath;
                AppLog.Info($"TTS audio generated ({audioBytes.Length / 1024} KB) for chunk {_currentIndex + 1}/{_chunks.Count}", "MistralTTS");
            }

            // Preload next chunk in background if available
            _ = PreloadNextChunkAsync(_currentIndex + 1);

            UpdateState(AppProcessingState.Playing, $"Playing part {_currentIndex + 1}/{_chunks.Count}", _currentIndex, _chunks.Count);
            AppLog.Info($"Playing audio part {_currentIndex + 1}/{_chunks.Count}", "Audio");
            await _audioPlayer.PlayFileAsync(audioFilePath);
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Playback was canceled.", "Audio");
            UpdateState(AppProcessingState.Idle, "Playback canceled.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"TTS playback error on part {_currentIndex + 1}: {ex.Message}", ex, "MistralTTS");
            UpdateState(AppProcessingState.Error, $"TTS Error: {ex.Message}", _currentIndex, _chunks.Count);
        }
    }

    private async Task PreloadNextChunkAsync(int nextIndex)
    {
        if (nextIndex >= _chunks.Count || _cachedAudioFiles.ContainsKey(nextIndex))
            return;

        try
        {
            string nextText = _chunks[nextIndex];
            byte[] audioBytes = await _mistralClient.GenerateSpeechAsync(
                _settingsService.MistralApiKey,
                nextText,
                _settingsService.SelectedVoiceId,
                _settingsService.TtsModel,
                CancellationToken.None);

            string path = SaveAudioToCache(nextIndex, audioBytes);
            _cachedAudioFiles[nextIndex] = path;
        }
        catch
        {
            // Background preloading error can be safely retried on demand
        }
    }

    private void OnPlaybackEnded()
    {
        if (_settingsService.AutoPlay && _currentIndex + 1 < _chunks.Count)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await PlayChunkAsync(_currentIndex + 1, _cts?.Token ?? CancellationToken.None);
                }
                catch (Exception ex)
                {
                    UpdateState(AppProcessingState.Error, $"Playback Error: {ex.Message}");
                }
            });
        }
        else if (_currentIndex + 1 >= _chunks.Count)
        {
            UpdateState(AppProcessingState.Idle, "Completed playback of all parts.");
        }
        else
        {
            UpdateState(AppProcessingState.Paused, $"Finished part {_currentIndex + 1}/{_chunks.Count}", _currentIndex, _chunks.Count);
        }
    }

    public void Pause()
    {
        _audioPlayer.Pause();
        UpdateState(AppProcessingState.Paused, $"Paused at part {_currentIndex + 1}/{_chunks.Count}", _currentIndex, _chunks.Count);
    }

    public void Resume()
    {
        _audioPlayer.Resume();
        UpdateState(AppProcessingState.Playing, $"Playing part {_currentIndex + 1}/{_chunks.Count}", _currentIndex, _chunks.Count);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _audioPlayer.Stop();
        _chunks.Clear();
        _cachedAudioFiles.Clear();
        _currentIndex = 0;
        UpdateState(AppProcessingState.Idle, "Ready");
    }

    public async Task NextAsync()
    {
        if (_currentIndex + 1 < _chunks.Count)
        {
            await PlayChunkAsync(_currentIndex + 1, _cts?.Token ?? CancellationToken.None);
        }
    }

    public async Task PreviousAsync()
    {
        if (_currentIndex - 1 >= 0)
        {
            await PlayChunkAsync(_currentIndex - 1, _cts?.Token ?? CancellationToken.None);
        }
    }

    private void UpdateState(AppProcessingState state, string message, int current = 0, int total = 0)
    {
        _currentState = state;
        StatusChanged?.Invoke(this, new ProcessingStatusChangedEventArgs(state, message, current, total));
    }

    private static string SaveAudioToCache(int chunkIndex, byte[] bytes)
    {
        string cacheDir = Path.Combine(FileSystem.CacheDirectory, "VoceAudioCache");
        if (!Directory.Exists(cacheDir))
            Directory.CreateDirectory(cacheDir);

        string filename = $"chunk_{chunkIndex}_{Guid.NewGuid():N}.mp3";
        string filePath = Path.Combine(cacheDir, filename);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }
}
