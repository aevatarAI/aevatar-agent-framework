namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Helpers;

/// <summary>
/// Masks API keys for safe display in API responses.
/// Shows first 4 and last 4 characters with asterisks in between.
/// </summary>
public static class ApiKeyMasker
{
    private const int MinLengthForPartialMask = 12;
    private const int VisibleChars = 4;
    private const string FullMask = "****";

    /// <summary>
    /// Masks an API key for display.
    /// </summary>
    /// <param name="apiKey">The plaintext API key to mask.</param>
    /// <returns>Masked string (e.g., "sk-p****i789").</returns>
    public static string Mask(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return FullMask;

        if (apiKey.Length < MinLengthForPartialMask)
            return FullMask;

        var prefix = apiKey[..VisibleChars];
        var suffix = apiKey[^VisibleChars..];
        var middleLength = apiKey.Length - (VisibleChars * 2);
        var masked = new string('*', Math.Min(middleLength, 28));

        return $"{prefix}{masked}{suffix}";
    }
}
