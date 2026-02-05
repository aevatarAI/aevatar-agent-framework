namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Encryption;

/// <summary>
/// Options for the AES-256-GCM encryption service.
/// Master key is loaded from environment variable at startup.
/// </summary>
public sealed class UserProviderEncryptionOptions
{
    /// <summary>Base64-encoded 32-byte AES-256 master key.</summary>
    public string MasterKeyBase64 { get; set; } = string.Empty;
}
