using VoceLens.Models;

namespace VoceLens.Services.Mistral;

public interface IMistralClient
{
    Task<List<VoiceItem>> GetVoicesAsync(string apiKey, CancellationToken cancellationToken = default);
    Task<string> ExtractTextFromScreenshotAsync(string apiKey, byte[] imageBytes, string? model = null, CancellationToken cancellationToken = default);
    Task<byte[]> GenerateSpeechAsync(string apiKey, string text, string? voiceId = null, string? model = null, CancellationToken cancellationToken = default);
}
