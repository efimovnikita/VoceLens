namespace VoceLens.Models;

public enum AppProcessingState
{
    Idle,
    CapturingScreen,
    ScanningOcr,
    TextChunking,
    GeneratingAudio,
    Playing,
    Paused,
    Error
}

public class ProcessingStatusChangedEventArgs : EventArgs
{
    public AppProcessingState State { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CurrentChunkIndex { get; set; }
    public int TotalChunks { get; set; }

    public ProcessingStatusChangedEventArgs(AppProcessingState state, string message, int currentChunk = 0, int total = 0)
    {
        State = state;
        Message = message;
        CurrentChunkIndex = currentChunk;
        TotalChunks = total;
    }
}
