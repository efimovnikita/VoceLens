using System.Text.Json.Serialization;

namespace VoceLens.Models;

public class VoiceListResponse
{
    [JsonPropertyName("items")]
    public List<VoiceItem> Items { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class VoiceItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("languages")]
    public List<string>? Languages { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("age")]
    public int? Age { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    // Helper display property for UI pickers
    [JsonIgnore]
    public string DisplayName => !string.IsNullOrEmpty(UserId) 
        ? $"⭐ {Name} (Your Custom Voice)" 
        : $"🎙️ {Name}";
}
