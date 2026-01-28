using Microsoft.Extensions.Logging;
namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Abstraction for verifying knowledge nodes using LLM-based reasoning.
/// </summary>
public interface IKnowledgeNodeVerifier
{
    /// <summary>
    /// Verifies whether a knowledge node is still valid.
    /// </summary>
    /// <param name="input">Verification input containing node details and dependencies.</param>
    /// <param name="progress">Optional progress reporter for token streaming.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result indicating pass/fail and reasoning.</returns>
    Task<VerificationResult> VerifyAsync(
        VerificationInput input,
        IProgress<VerificationProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Input for knowledge node verification.
/// </summary>
public sealed record VerificationInput
{
    /// <summary>
    /// The node ID being verified.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// The node's core description/label.
    /// </summary>
    public required string NodeLabel { get; init; }

    /// <summary>
    /// The node's explanation content for verification.
    /// </summary>
    public required string ExplanationContent { get; init; }

    /// <summary>
    /// IDs of dependency nodes (ancestors).
    /// </summary>
    public required IReadOnlyList<string> DependencyIds { get; init; }

    /// <summary>
    /// Labels/descriptions of dependency nodes for context.
    /// </summary>
    public required IReadOnlyList<string> DependencyLabels { get; init; }

    /// <summary>
    /// Optional LLM provider name override.
    /// </summary>
    public string? LLMProviderName { get; init; }

    /// <summary>
    /// Optional timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; init; }
}

/// <summary>
/// Result of knowledge node verification.
/// </summary>
public sealed record VerificationResult
{
    /// <summary>
    /// Whether the verification passed (true) or failed (false).
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// The review result status.
    /// </summary>
    public required ReviewResult Result { get; init; }

    /// <summary>
    /// Reason for failure (if any).
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Full verification content/reasoning from LLM.
    /// </summary>
    public string? VerificationContent { get; init; }

    /// <summary>
    /// Error message if verification could not complete.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Duration of verification.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Progress report during verification (for token streaming).
/// </summary>
public sealed record VerificationProgress
{
    /// <summary>
    /// Agent ID producing the token.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Role of the agent (coordinator or worker).
    /// </summary>
    public required string AgentRole { get; init; }

    /// <summary>
    /// Token content.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Whether this is the final token.
    /// </summary>
    public bool IsComplete { get; init; }
}
