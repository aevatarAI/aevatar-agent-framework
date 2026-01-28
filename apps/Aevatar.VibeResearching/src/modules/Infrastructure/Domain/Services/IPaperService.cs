using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Domain interface for paper service.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface IPaperService
{
    /// <summary>
    /// Ensures paper files exist for session.
    /// </summary>
    Task<WorkspacePaths> EnsurePaperFilesAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Applies a patch to a paper file.
    /// </summary>
    Task ApplyPatchAsync(string sessionId, string? runId, PaperPatchProposal proposal, CancellationToken ct);
}
