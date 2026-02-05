using System.Security.Cryptography;
using Aevatar.VibeResearching.UserProviders.Infrastructure.Encryption;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.Encryption;

/// <summary>
/// Unit tests for the AES-256-GCM encryption service.
/// Covers round-trip, invalid key, tampered data, and edge cases.
/// </summary>
public sealed class Aes256GcmEncryptionServiceTests
{
    private static Aes256GcmEncryptionService CreateService(byte[]? key = null)
    {
        key ??= RandomNumberGenerator.GetBytes(32);
        var base64Key = Convert.ToBase64String(key);
        var options = Options.Create(new UserProviderEncryptionOptions { MasterKeyBase64 = base64Key });
        return new Aes256GcmEncryptionService(options);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_ReturnsOriginalPlaintext()
    {
        // Arrange
        var service = CreateService();
        var plaintext = "sk-proj-abc123def456ghi789jkl012mno345pqr678stu901vwx234yz";

        // Act
        var encrypted = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(encrypted);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        // Arrange
        var service = CreateService();
        var plaintext = "test-api-key-12345";

        // Act
        var encrypted1 = service.Encrypt(plaintext);
        var encrypted2 = service.Encrypt(plaintext);

        // Assert -- different nonces produce different ciphertexts
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        // Arrange
        var key1 = RandomNumberGenerator.GetBytes(32);
        var key2 = RandomNumberGenerator.GetBytes(32);
        var service1 = CreateService(key1);
        var service2 = CreateService(key2);
        var plaintext = "test-api-key";

        // Act
        var encrypted = service1.Encrypt(plaintext);

        // Assert
        Assert.Throws<CryptographicException>(() => service2.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_ThrowsCryptographicException()
    {
        // Arrange
        var service = CreateService();
        var plaintext = "test-api-key";
        var encrypted = service.Encrypt(plaintext);

        // Tamper by modifying the base64-decoded ciphertext bytes directly
        var envelope = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(encrypted);
        var ctBase64 = envelope.GetProperty("ct").GetString()!;
        var ctBytes = Convert.FromBase64String(ctBase64);
        ctBytes[0] ^= 0xFF; // Flip bits in first byte
        var tamperedCt = Convert.ToBase64String(ctBytes);

        var tampered = encrypted.Replace($"\"ct\":\"{ctBase64}\"", $"\"ct\":\"{tamperedCt}\"");

        // Assert
        Assert.ThrowsAny<CryptographicException>(() => service.Decrypt(tampered));
    }

    [Fact]
    public void Constructor_WithInvalidKeyLength_ThrowsInvalidOperationException()
    {
        // Arrange -- 16 bytes instead of 32
        var shortKey = RandomNumberGenerator.GetBytes(16);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => CreateService(shortKey));
    }

    [Fact]
    public void Constructor_WithEmptyKey_ThrowsException()
    {
        // Arrange
        var options = Options.Create(new UserProviderEncryptionOptions { MasterKeyBase64 = "" });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new Aes256GcmEncryptionService(options));
    }

    [Fact]
    public void Encrypt_EmptyString_EncryptsAndDecryptsSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var plaintext = "";

        // Act
        var encrypted = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(encrypted);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_LargeInput_EncryptsAndDecryptsSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var plaintext = new string('A', 500); // Max API key length

        // Act
        var encrypted = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(encrypted);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_InvalidJson_ThrowsException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => service.Decrypt("not-valid-json"));
    }

    [Fact]
    public void Decrypt_UnsupportedVersion_ThrowsCryptographicException()
    {
        // Arrange
        var service = CreateService();
        var invalidEnvelope = """{"v":99,"alg":"A256GCM","nonce":"AAAA","tag":"BBBB","ct":"CCCC"}""";

        // Act & Assert
        Assert.Throws<CryptographicException>(() => service.Decrypt(invalidEnvelope));
    }
}
