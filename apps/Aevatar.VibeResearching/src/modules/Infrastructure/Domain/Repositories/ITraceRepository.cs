using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Repository interface for derivation trace operations.
/// Stores round-by-round summaries of research progress.
/// </summary>
public interface ITraceRepository
{
    /// <summary>
    /// Gets the file path for the trace JSONL file.
    /// </summary>
    string GetTracePath(string sessionId);

    /// <summary>
    /// Appends a round summary to the trace.
    /// Also writes a human-readable summary markdown file for the run.
    /// </summary>
    Task AppendAsync(string sessionId, SraRoundSummary summary, string? summaryMarkdown, CancellationToken ct);

    /// <summary>
    /// Loads the latest N round summaries (bounded tail scan).
    /// </summary>
    Task<List<SraRoundSummary>> LoadLatestAsync(string sessionId, int max, CancellationToken ct);

    /// <summary>
    /// Gets round summaries formatted for UI snapshot display.
    /// </summary>
    Task<object> GetSnapshotForUiAsync(string sessionId, int max, CancellationToken ct);
}
