using System.Text;

namespace VoceLens.Services.Logging;

public class AppLogService : IAppLogService
{
    private const int MaxEntries = 500;
    private readonly LinkedList<AppLogEntry> _entries = new();
    private readonly object _lock = new();

    public event EventHandler? LogsChanged;

    public AppLogService()
    {
        AppLog.Instance = this;
        LogInfo("VoceLens application log initialized.", "App");
    }

    public void Log(string message, string source = "App", AppLogLevel level = AppLogLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var entry = new AppLogEntry(DateTime.Now, level, source, message.Trim());

        lock (_lock)
        {
            if (_entries.Count >= MaxEntries)
            {
                _entries.RemoveFirst();
            }
            _entries.AddLast(entry);
        }

#if ANDROID
        switch (level)
        {
            case AppLogLevel.Error:
                Android.Util.Log.Error($"VoceLens.{source}", message);
                break;
            case AppLogLevel.Warn:
                Android.Util.Log.Warn($"VoceLens.{source}", message);
                break;
            case AppLogLevel.Debug:
                Android.Util.Log.Debug($"VoceLens.{source}", message);
                break;
            default:
                Android.Util.Log.Info($"VoceLens.{source}", message);
                break;
        }
#endif

        LogsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LogInfo(string message, string source = "App") => Log(message, source, AppLogLevel.Info);

    public void LogWarn(string message, string source = "App") => Log(message, source, AppLogLevel.Warn);

    public void LogError(string message, Exception? ex = null, string source = "App")
    {
        string fullMessage = ex != null ? $"{message} ({ex.GetType().Name}: {ex.Message})" : message;
        Log(fullMessage, source, AppLogLevel.Error);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
        LogsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<AppLogEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    public string GetFormattedLogText(bool newestFirst = true)
    {
        List<AppLogEntry> snapshot;
        lock (_lock)
        {
            snapshot = _entries.ToList();
        }

        if (snapshot.Count == 0)
            return "No log events recorded yet.";

        if (newestFirst)
        {
            snapshot.Reverse();
        }

        var sb = new StringBuilder(snapshot.Count * 64);
        foreach (var entry in snapshot)
        {
            sb.AppendLine(entry.ToString());
        }

        return sb.ToString().TrimEnd();
    }
}

public static class AppLog
{
    public static IAppLogService? Instance { get; set; }

    public static void Info(string message, string source = "App") => Instance?.LogInfo(message, source);
    public static void Warn(string message, string source = "App") => Instance?.LogWarn(message, source);
    public static void Error(string message, Exception? ex = null, string source = "App") => Instance?.LogError(message, ex, source);
}
