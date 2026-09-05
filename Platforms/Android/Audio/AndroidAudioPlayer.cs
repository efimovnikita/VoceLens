#if ANDROID
using Android.Media;
using VoceLens.Services.Audio;

namespace VoceLens.Platforms.Android.Audio;

public class AndroidAudioPlayer : IPlatformAudioPlayer
{
    private MediaPlayer? _mediaPlayer;
    private readonly object _lock = new();

    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    return _mediaPlayer?.IsPlaying ?? false;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public event Action? PlaybackEnded;

    public Task PlayFileAsync(string filePath)
    {
        var tcs = new TaskCompletionSource<bool>();

        lock (_lock)
        {
            try
            {
                StopInternal();

                _mediaPlayer = new MediaPlayer();
                _mediaPlayer.SetAudioAttributes(
                    new AudioAttributes.Builder()
                        .SetContentType(AudioContentType.Speech)!
                        .SetUsage(AudioUsageKind.Media)!
                        .Build()
                );

                _mediaPlayer.SetDataSource(filePath);

                _mediaPlayer.Prepared += (sender, args) =>
                {
                    try
                    {
                        _mediaPlayer?.Start();
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                };

                _mediaPlayer.Completion += (sender, args) =>
                {
                    PlaybackEnded?.Invoke();
                };

                _mediaPlayer.Error += (sender, args) =>
                {
                    tcs.TrySetException(new InvalidOperationException($"Android MediaPlayer Error: {args.What}"));
                };

                _mediaPlayer.PrepareAsync();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        return tcs.Task;
    }

    public void Pause()
    {
        lock (_lock)
        {
            try
            {
                if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Pause();
                }
            }
            catch
            {
                // Ignore state exception
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            try
            {
                if (_mediaPlayer != null && !_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Start();
                }
            }
            catch
            {
                // Ignore state exception
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        try
        {
            if (_mediaPlayer != null)
            {
                if (_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Stop();
                }
                _mediaPlayer.Reset();
                _mediaPlayer.Release();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }
        }
        catch
        {
            _mediaPlayer = null;
        }
    }
}
#endif
