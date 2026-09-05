using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VoceLens.Models;

namespace VoceLens.Services.Mistral;

public class MistralClient : IMistralClient
{
    private readonly HttpClient _httpClient;
    private const string BaseApiUrl = "https://api.mistral.ai/v1";

    public MistralClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Fetches voices from Mistral audio voices endpoint.
    /// Prioritizes user's custom voices (voices with non-empty user_id, matching Voce),
    /// and also provides standard voices so the list is never empty if the user hasn't created a cloned voice yet.
    /// </summary>
    public async Task<List<VoiceItem>> GetVoicesAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Mistral API Key is required.", nameof(apiKey));

        string cleanKey = apiKey.Trim();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseApiUrl}/audio/voices?limit=50&offset=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cleanKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            HandleApiError(response.StatusCode, responseContent);
        }

        var voiceListResponse = JsonSerializer.Deserialize<VoiceListResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var allVoices = voiceListResponse?.Items ?? new List<VoiceItem>();

        // Sort voices: user's custom voices first (voices with user_id, matching Voce)
        var customVoices = allVoices.Where(v => !string.IsNullOrEmpty(v.UserId)).ToList();
        var standardVoices = allVoices.Where(v => string.IsNullOrEmpty(v.UserId)).ToList();

        var result = new List<VoiceItem>();
        result.AddRange(customVoices);
        result.AddRange(standardVoices);

        return result;
    }

    /// <summary>
    /// Extracts text from screenshot image bytes using Mistral OCR API,
    /// matching Voce's extractTextFromScreenshot.
    /// </summary>
    public async Task<string> ExtractTextFromScreenshotAsync(string apiKey, byte[] imageBytes, string? model = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Mistral API Key is required for OCR.", nameof(apiKey));

        if (imageBytes == null || imageBytes.Length == 0)
            throw new ArgumentException("Screenshot image data is empty.", nameof(imageBytes));

        string base64Image = Convert.ToBase64String(imageBytes);
        string dataUrl = $"data:image/jpeg;base64,{base64Image}";

        var ocrRequest = new MistralOcrRequest
        {
            Model = string.IsNullOrWhiteSpace(model) ? "mistral-ocr-latest" : model.Trim(),
            Document = new MistralOcrDocument
            {
                Type = "image_url",
                ImageUrl = dataUrl
            }
        };

        string jsonPayload = JsonSerializer.Serialize(ocrRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseApiUrl}/ocr")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            HandleApiError(response.StatusCode, responseContent);
        }

        var ocrResponse = JsonSerializer.Deserialize<MistralOcrResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (ocrResponse?.Pages != null && ocrResponse.Pages.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var page in ocrResponse.Pages)
            {
                if (!string.IsNullOrWhiteSpace(page.Markdown))
                {
                    sb.AppendLine(page.Markdown.Trim());
                }
            }
            return sb.ToString().Trim();
        }

        return string.Empty;
    }

    /// <summary>
    /// Synthesizes text chunk into MP3 audio bytes using Mistral Voxtral TTS API,
    /// matching Voce's generateSpeechStreaming.
    /// </summary>
    public async Task<byte[]> GenerateSpeechAsync(string apiKey, string text, string? voiceId = null, string? model = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Mistral API Key is required for TTS.", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text input for TTS cannot be empty.", nameof(text));

        var speechRequest = new MistralSpeechRequest
        {
            Model = string.IsNullOrWhiteSpace(model) ? "voxtral-mini-tts-2603" : model.Trim(),
            Input = text.Trim(),
            VoiceId = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim(),
            ResponseFormat = "mp3"
        };

        string jsonPayload = JsonSerializer.Serialize(speechRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseApiUrl}/audio/speech")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            HandleApiError(response.StatusCode, errorBody);
        }

        // Mistral Speech endpoint returns JSON {"audio_data": "<base64>"}
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("audio_data", out var audioDataProp) ||
                doc.RootElement.TryGetProperty("audioData", out audioDataProp))
            {
                string? base64 = audioDataProp.GetString();
                if (!string.IsNullOrEmpty(base64))
                {
                    return Convert.FromBase64String(base64);
                }
            }
        }
        catch (JsonException)
        {
            // If response happens to be binary audio stream directly:
            return Encoding.UTF8.GetBytes(responseContent);
        }

        throw new InvalidOperationException("Failed to decode audio data from Mistral TTS response.");
    }

    private static void HandleApiError(System.Net.HttpStatusCode statusCode, string content)
    {
        string lower = content.ToLowerInvariant();
        if (lower.Contains("safety") || lower.Contains("policy") || lower.Contains("moderation") || lower.Contains("blocked"))
        {
            throw new HttpRequestException("Content blocked by Mistral safety filters.");
        }
        if (statusCode == System.Net.HttpStatusCode.Unauthorized || lower.Contains("unauthorized") || lower.Contains("api key"))
        {
            throw new HttpRequestException("Invalid Mistral API Key. Please check your key in Settings.");
        }
        if (statusCode == (System.Net.HttpStatusCode)429)
        {
            throw new HttpRequestException("Mistral API rate limit exceeded. Please wait a moment.");
        }

        throw new HttpRequestException($"Mistral API Error (HTTP {(int)statusCode}): {content}");
    }
}
