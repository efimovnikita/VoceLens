using System.Text.Json.Serialization;

namespace VoceLens.Models;

public class MistralSpeechRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "voxtral-mini-tts-2603";

    [JsonPropertyName("input")]
    public string Input { get; set; } = string.Empty;

    [JsonPropertyName("voice_id")]
    public string? VoiceId { get; set; }

    [JsonPropertyName("response_format")]
    public string ResponseFormat { get; set; } = "mp3";

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class MistralSpeechResponse
{
    [JsonPropertyName("audio_data")]
    public string? AudioData { get; set; }
}
