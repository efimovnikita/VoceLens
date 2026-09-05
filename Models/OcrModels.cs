using System.Text.Json.Serialization;

namespace VoceLens.Models;

public class MistralOcrRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "mistral-ocr-latest";

    [JsonPropertyName("document")]
    public MistralOcrDocument Document { get; set; } = new();
}

public class MistralOcrDocument
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "image_url";

    [JsonPropertyName("image_url")]
    public string ImageUrl { get; set; } = string.Empty;
}

public class MistralOcrResponse
{
    [JsonPropertyName("pages")]
    public List<MistralOcrPage> Pages { get; set; } = new();
}

public class MistralOcrPage
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }
}
