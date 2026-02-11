namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// Resolved image data for LLM multimodal requests.
/// Converted from image keys (blob storage references) at runtime.
/// </summary>
public class AevatarImageData
{
    /// <summary>
    /// Original key from blob storage
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Raw image bytes
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; }

    /// <summary>
    /// MIME type (e.g., "image/jpeg", "image/png")
    /// </summary>
    public string MediaType { get; set; } = "image/jpeg";
}
