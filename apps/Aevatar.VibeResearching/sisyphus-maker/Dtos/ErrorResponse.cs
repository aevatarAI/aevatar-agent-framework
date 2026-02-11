namespace SisyphusMaker.Dtos;

using System.Text.Json.Serialization;

/// <summary>
/// Standard error response envelope.
/// </summary>
public sealed class ErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}
