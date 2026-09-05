using VoceLens.Models;

namespace VoceLens.Services.Audio;

public interface IAudioPlaybackManager
{
    IReadOnlyList<string> Chunks { get; }
    int CurrentChunkIndex { get; }
    AppProcessingState CurrentState { get; }
    event EventHandler<ProcessingStatusChangedEventArgs>? StatusChanged;

    Task StartReadingTextAsync(string fullText, CancellationToken cancellationToken = default);
    Task PlayChunkAsync(int index, CancellationToken cancellationToken = default);
    void Pause();
    void Resume();
    void Stop();
    Task NextAsync();
    Task PreviousAsync();
}
