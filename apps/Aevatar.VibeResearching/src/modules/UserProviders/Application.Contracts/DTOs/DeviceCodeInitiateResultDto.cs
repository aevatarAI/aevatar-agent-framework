namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Returned to the frontend after requesting a device code from OpenAI.
/// The user must visit <see cref="VerificationUri"/> and enter <see cref="UserCode"/>.
/// </summary>
public sealed class DeviceCodeInitiateResultDto
{
    /// <summary>Device auth session ID used for polling (frontend sends this back).</summary>
    public string DeviceAuthId { get; set; } = string.Empty;

    /// <summary>Short code the user enters at the verification URI.</summary>
    public string UserCode { get; set; } = string.Empty;

    /// <summary>URL where the user authorizes (e.g., https://auth.openai.com/codex/device).</summary>
    public string VerificationUri { get; set; } = string.Empty;

    /// <summary>Minimum polling interval in seconds.</summary>
    public int Interval { get; set; }
}
