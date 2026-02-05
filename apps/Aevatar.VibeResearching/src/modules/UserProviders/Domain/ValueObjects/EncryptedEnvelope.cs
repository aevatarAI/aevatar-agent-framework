using System.Text.Json.Serialization;

namespace Aevatar.VibeResearching.UserProviders.ValueObjects;

/// <summary>
/// JSON-serializable envelope for AES-256-GCM encrypted data.
/// Stored as a string in MongoDB fields.
/// </summary>
public sealed class EncryptedEnvelope
{
    /// <summary>Schema version (always 1).</summary>
    [JsonPropertyName("v")]
    public int V { get; set; } = 1;

    /// <summary>Algorithm identifier.</summary>
    [JsonPropertyName("alg")]
    public string Alg { get; set; } = "A256GCM";

    /// <summary>Base64-encoded nonce (12 bytes).</summary>
    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    /// <summary>Base64-encoded authentication tag (16 bytes).</summary>
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    /// <summary>Base64-encoded ciphertext.</summary>
    [JsonPropertyName("ct")]
    public string Ct { get; set; } = string.Empty;
}
