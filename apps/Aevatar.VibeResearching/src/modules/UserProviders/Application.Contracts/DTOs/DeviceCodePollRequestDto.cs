namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Request body for polling the device code auth endpoint.
/// </summary>
public sealed class DeviceCodePollRequestDto
{
    /// <summary>The device_auth_id returned from the initiate step.</summary>
    public string DeviceAuthId { get; set; } = string.Empty;

    /// <summary>The user_code returned from the initiate step.</summary>
    public string UserCode { get; set; } = string.Empty;
}
