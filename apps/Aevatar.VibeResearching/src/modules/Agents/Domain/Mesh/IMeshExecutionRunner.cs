using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Orchestration;
using Aevatar.VibeResearching.Agents.Mesh.ValueObjects;

namespace Aevatar.VibeResearching.Agents.Mesh;

/// <summary>
/// Domain interface for mesh execution runner.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface IMeshExecutionRunner
{
    /// <summary>
    /// Executes mesh plan.
    /// </summary>
    Task<MeshRunResult> ExecuteAsync(
        ResearchSession session,
        SessionInputInDto input,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        MeshExecutionPlan plan,
        string question,
        Func<string, string?> resolveProviderName,
        CancellationToken ct);
}
