using System.Text.Json.Serialization;

namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Represents a research session that groups related nodes in the knowledge graph.
/// Sessions provide isolation and context for research activities.
/// </summary>
public sealed class Session
{
    /// <summary>
    /// Unique identifier for this session.
    /// Format: session-{guid}
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// When this session was started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When this session was ended (null if still active).
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Current status of this session.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionStatus Status { get; set; } = SessionStatus.Active;

    /// <summary>
    /// Creates a new active session with the specified ID.
    /// </summary>
    /// <param name="sessionId">Optional session ID. If not provided, a new GUID-based ID is generated.</param>
    /// <returns>A new Session instance.</returns>
    public static Session Create(string? sessionId = null)
    {
        return new Session
        {
            Id = sessionId ?? $"session-{Guid.NewGuid():N}",
            StartedAt = DateTimeOffset.UtcNow,
            Status = SessionStatus.Active
        };
    }

    /// <summary>
    /// Ends the session with the specified status.
    /// </summary>
    /// <param name="status">The final status (Completed or Abandoned).</param>
    /// <exception cref="InvalidOperationException">Thrown if session is not active.</exception>
    public void End(SessionStatus status = SessionStatus.Completed)
    {
        if (Status != SessionStatus.Active)
        {
            throw new InvalidOperationException($"Cannot end session with status {Status}. Session must be Active.");
        }

        if (status == SessionStatus.Active)
        {
            throw new ArgumentException("Cannot end session with Active status.", nameof(status));
        }

        EndedAt = DateTimeOffset.UtcNow;
        Status = status;
    }
}
