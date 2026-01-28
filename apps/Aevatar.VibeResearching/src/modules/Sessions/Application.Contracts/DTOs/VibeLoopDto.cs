namespace Aevatar.VibeResearching.Sessions.DTOs;

/// <summary>
/// Configuration for vibe_loop mode execution.
/// </summary>
public sealed class VibeLoopDto
{
    /// <summary>
    /// Hard cap for loop rounds (safety valve).
    /// </summary>
    public int? MaxIterations { get; init; }

    /// <summary>
    /// Total wall-clock budget for the whole loop (milliseconds).
    /// </summary>
    public int? MaxTotalDurationMs { get; init; }
}
