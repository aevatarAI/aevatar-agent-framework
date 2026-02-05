namespace Aevatar.VibeResearching.UserProviders.Services;

/// <summary>
/// Encrypts and decrypts API keys using AES-256-GCM.
/// Registered as singleton to avoid repeated key parsing.
/// </summary>
public interface IUserProviderEncryptionService
{
    /// <summary>
    /// Encrypts plaintext into a JSON envelope string.
    /// </summary>
    /// <param name="plaintext">The plaintext API key.</param>
    /// <returns>JSON-encoded EncryptedEnvelope string.</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a JSON envelope string back to plaintext.
    /// </summary>
    /// <param name="encryptedEnvelope">JSON-encoded EncryptedEnvelope string.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// If decryption fails (tampered data, wrong key, etc.).
    /// </exception>
    string Decrypt(string encryptedEnvelope);
}
