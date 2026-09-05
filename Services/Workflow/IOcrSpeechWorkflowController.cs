using VoceLens.Models;

namespace VoceLens.Services.Workflow;

public interface IOcrSpeechWorkflowController
{
    AppProcessingState CurrentState { get; }
    string CurrentStatusMessage { get; }
    string LastExtractedText { get; }
    event EventHandler<ProcessingStatusChangedEventArgs>? StatusChanged;

    Task ProcessScreenCaptureAndReadAsync(CancellationToken cancellationToken = default);
    Task ProcessImageBytesAndReadAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
    Task ProcessCustomTextAndReadAsync(string text, CancellationToken cancellationToken = default);
    void StopPlaybackAndClear();
}
