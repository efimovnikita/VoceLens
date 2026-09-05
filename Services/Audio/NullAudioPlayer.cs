namespace VoceLens.Services.Audio;

public class NullAudioPlayer : IPlatformAudioPlayer
{
    public bool IsPlaying { get; private set; }
    public event Action? PlaybackEnded;

    public async Task PlayFileAsync(string filePath)
    {
        IsPlaying = true;
        // Simulate playback delay for non-android platforms
        await Task.Delay(2000);
        IsPlaying = false;
        PlaybackEnded?.Invoke();
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void Resume()
    {
        IsPlaying = true;
    }

    public void Stop()
    {
        IsPlaying = false;
    }
}
