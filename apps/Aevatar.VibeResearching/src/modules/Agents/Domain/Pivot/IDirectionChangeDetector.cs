using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Pivot.Models;

namespace Aevatar.VibeResearching.Agents.Pivot;

/// <summary>
/// Interface for detecting research direction change intent from user messages.
/// </summary>
public interface IDirectionChangeDetector
{
    /// <summary>
    /// Analyzes a user message to detect if it indicates a research direction change.
    /// </summary>
    /// <param name="sessionId">Research session identifier.</param>
    /// <param name="messageId">Unique message identifier.</param>
    /// <param name="userMessage">The user's message text to analyze.</param>
    /// <param name="currentDirection">Optional current research direction for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detection result with confidence and extracted intent details.</returns>
    Task<DirectionChangeIntent> DetectAsync(
        string sessionId,
        string messageId,
        string userMessage,
        string? currentDirection = null,
        CancellationToken cancellationToken = default);
}
