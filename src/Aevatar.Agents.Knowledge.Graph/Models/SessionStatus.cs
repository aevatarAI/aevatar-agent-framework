using System.Text.Json.Serialization;

namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Status of a research session.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionStatus
{
    /// <summary>
    /// Session is currently active and accepting operations.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Session has been completed normally via explicit end call.
    /// </summary>
    Completed = 1,

    /// <summary>
    /// Session was terminated without proper completion.
    /// </summary>
    Abandoned = 2
}
