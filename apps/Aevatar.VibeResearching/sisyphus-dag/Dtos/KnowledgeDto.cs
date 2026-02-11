namespace SisyphusDag.Dtos;

using System.Text.Json.Serialization;

/// <summary>
/// Input DTO for a single knowledge item (create or update).
/// </summary>
public sealed class KnowledgeDto
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("deriveDetail")]
    public string DeriveDetail { get; set; } = string.Empty;

    [JsonPropertyName("resourceUri")]
    public string? ResourceUri { get; set; }

    [JsonPropertyName("references")]
    public List<string>? References { get; set; }

    [JsonPropertyName("dependsOn")]
    public List<string>? DependsOn { get; set; }
}
