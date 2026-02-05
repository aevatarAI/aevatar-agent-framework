using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aevatar.VibeResearching.UserProviders.Constants;
using Aevatar.VibeResearching.UserProviders.Services;
using Aevatar.VibeResearching.UserProviders.ValueObjects;
using Microsoft.Extensions.Options;

namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Encryption;

/// <summary>
/// AES-256-GCM encryption service for user provider API keys.
/// Registered as singleton; master key is parsed once at construction.
/// Reuses the same cryptographic pattern from AevatarUserSecrets.cs with a
/// different AAD and server-side master key from environment variable.
/// </summary>
public sealed class Aes256GcmEncryptionService : IUserProviderEncryptionService, IDisposable
{
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private static readonly byte[] AadBytes = Encoding.UTF8.GetBytes(UserProviderConsts.EncryptionAad);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly byte[] _masterKey;

    public Aes256GcmEncryptionService(IOptions<UserProviderEncryptionOptions> options)
    {
        var keyBase64 = options.Value.MasterKeyBase64;

        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            throw new InvalidOperationException(
                "Master key is required. Set AEVATAR_USER_PROVIDER_MASTER_KEY environment variable.");
        }

        _masterKey = Convert.FromBase64String(keyBase64);

        if (_masterKey.Length != KeyBytes)
        {
            throw new InvalidOperationException(
                $"Master key must be exactly {KeyBytes} bytes. Got {_masterKey.Length}.");
        }
    }

    /// <inheritdoc />
    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagBytes];

        using var gcm = new AesGcm(_masterKey, TagBytes);
        gcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, AadBytes);

        var envelope = new EncryptedEnvelope
        {
            V = 1,
            Alg = "A256GCM",
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ct = Convert.ToBase64String(ciphertext)
        };

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    /// <inheritdoc />
    public string Decrypt(string encryptedEnvelope)
    {
        ArgumentNullException.ThrowIfNull(encryptedEnvelope);

        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(encryptedEnvelope, JsonOptions);

        if (envelope == null)
            throw new CryptographicException("Failed to deserialize encrypted envelope.");

        if (envelope.V != 1)
            throw new CryptographicException($"Unsupported envelope version: {envelope.V}");

        if (!string.Equals(envelope.Alg, "A256GCM", StringComparison.Ordinal))
            throw new CryptographicException($"Unsupported algorithm: {envelope.Alg}");

        var nonce = Convert.FromBase64String(envelope.Nonce);
        var tag = Convert.FromBase64String(envelope.Tag);
        var ciphertext = Convert.FromBase64String(envelope.Ct);

        if (nonce.Length != NonceBytes)
            throw new CryptographicException($"Invalid nonce length: {nonce.Length}");

        if (tag.Length != TagBytes)
            throw new CryptographicException($"Invalid tag length: {tag.Length}");

        var plaintext = new byte[ciphertext.Length];

        using var gcm = new AesGcm(_masterKey, TagBytes);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext, AadBytes);

        return Encoding.UTF8.GetString(plaintext);
    }

    public void Dispose()
    {
        // Clear master key from memory when service is disposed
        CryptographicOperations.ZeroMemory(_masterKey);
    }
}
