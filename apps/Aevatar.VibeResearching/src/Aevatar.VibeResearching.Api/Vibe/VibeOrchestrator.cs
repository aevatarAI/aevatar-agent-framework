using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.Core.Secrets;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VibeResearching.Api;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Delivery;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Goals;
using VibeResearching.Api.Vibe.Mesh;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;
using VibeResearching.Vibe.Pivot;

namespace VibeResearching.Api.Vibe;

// ============================================================
//  VibeOrchestrator (MVP)
//
//  Goal:
//  - Run one vibe round end-to-end:
//      research_assistant plan → workers → maker consensus → DAG + Trace
//
//  Notes:
//  - Best-effort: never crash server due to orchestration/projection.
//  - Streaming: caller provides emitAssistantDelta() that projects to AG-UI.
// ============================================================

internal sealed partial class VibeOrchestrator
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly VibeCore _core;
    private readonly VibePivot _pivot;
    private readonly VibeMesh _mesh;
    private readonly VibeHost _host;
    private readonly VibeWorkflowParsing _parsing;

    internal ResearchRuntime Runtime => _core.Runtime;

    public VibeOrchestrator(
        VibeCore core,
        VibePivot pivot,
        VibeMesh mesh,
        VibeHost host,
        VibeWorkflowParsing parsing)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _pivot = pivot ?? throw new ArgumentNullException(nameof(pivot));
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _parsing = parsing ?? throw new ArgumentNullException(nameof(parsing));
    }

    public async Task ExecuteOneRoundAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(materials);
        emitAssistantDelta ??= _ => { };

        var ctx = new VibeRoundContext(
            session,
            runId,
            input,
            question,
            materials,
            emitAssistantDelta);

        var dagId = session.EffectiveDagId;

        AgentProvidersStore.AgentProvidersSnapshot? agentProvidersSnap = null;
        try { agentProvidersSnap = await _core.AgentProviders.LoadAsync(session.Id, ct); } catch { /* best-effort */ }
        var agentProviderMap = agentProvidersSnap?.Map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? ResolveProvider(string agent)
        {
            if (!string.IsNullOrWhiteSpace(providerOverride))
                return providerOverride;

            var yamlProvider = _mesh.Roles.TryGetProvider(agent);
            if (!string.IsNullOrWhiteSpace(yamlProvider))
                return yamlProvider;

            if (agentProviderMap.TryGetValue(agent, out var p) && !string.IsNullOrWhiteSpace(p))
                return p;
            return session.ProviderName;
        }

        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var emptyPlan = new PlanResult(null, null, null);

        var meshUsed = await TryRunMeshWorkerPhaseAsync(
            ctx,
            dagId,
            emptyPlan,
            ResolveProvider,
            outputs,
            ct,
            _ => { });

        if (!meshUsed)
        {
            _host.Logger.LogDebug(
                "Mesh orchestration skipped for session {SessionId}; workflow handles remaining phases.",
                session.Id);
        }
    }

}
