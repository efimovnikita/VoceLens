namespace VoceLens.Services.Logging;

public enum AppLogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public record AppLogEntry(DateTime Timestamp, AppLogLevel Level, string Source, string Message)
{
    public override string ToString()
    {
        string levelStr = Level switch
        {
            AppLogLevel.Debug => "DEBUG",
            AppLogLevel.Info => "INFO ",
            AppLogLevel.Warn => "WARN ",
            AppLogLevel.Error => "ERROR",
            _ => "INFO "
        };
        return $"[{Timestamp:HH:mm:ss}] [{levelStr}] [{Source}] {Message}";
    }
}

public interface IAppLogService
{
    void Log(string message, string source = "App", AppLogLevel level = AppLogLevel.Info);
    void LogInfo(string message, string source = "App");
    void LogWarn(string message, string source = "App");
    void LogError(string message, Exception? ex = null, string source = "App");
    void Clear();
    IReadOnlyList<AppLogEntry> GetEntries();
    string GetFormattedLogText(bool newestFirst = true);
    event EventHandler? LogsChanged;
}
