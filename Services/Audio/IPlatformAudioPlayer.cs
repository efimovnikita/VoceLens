namespace VoceLens.Services.Audio;

public interface IPlatformAudioPlayer
{
    Task PlayFileAsync(string filePath);
    void Pause();
    void Resume();
    void Stop();
    bool IsPlaying { get; }
    event Action PlaybackEnded;
}
