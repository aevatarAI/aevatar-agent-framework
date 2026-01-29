using System.Text.RegularExpressions;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.AxiomReasoning.Models;
using Aevatar.AxiomReasoning.Graph;
using Aevatar.AxiomReasoning.Graph.Models;
using Aevatar.AxiomReasoning.EventStreaming.Events;

namespace Aevatar.AxiomReasoning.EventStreaming.AgUi;

// ============================================================
//  AG-UI BOOTSTRAP (Snapshots for reconnect)
//
//  WHY:
//  - SSE reconnect / new subscriber should not rely on replaying hundreds of progress/token events.
//  - Instead, provide deterministic snapshots:
//    - MESSAGES_SNAPSHOT: user input + last N completed assistant outputs
//    - STATE_SNAPSHOT: dependency graph (best-effort) from GraphStore
//
//  NOTE:
//  - Source of truth for UI hydration:
//    - Short-term: AIGAgentBase.State.History (bounded window; persisted via StateStore if configured)
//  - We do NOT rely on transcript.jsonl as a primary source (it's a debug artifact).
// ============================================================

public static class AxiomAgUiBootstrap
{
    public sealed record AgUiBootstrapMessages
    {
        public required List<AgUiMessage> Messages { get; init; }
        public List<AgUiEvent> ExtraEvents { get; init; } = [];
    }

    public static async Task<AgUiBootstrapMessages> BuildMessagesSnapshotAsync(
        AxiomSession session,
        IGAgentActorManager actorManager,
        int maxAssistantMessages,
        CancellationToken ct)
    {
        var messages = new List<AgUiMessage>(capacity: Math.Max(8, maxAssistantMessages + 1));
        var extra = new List<AgUiEvent>(capacity: Math.Max(8, maxAssistantMessages));

        // Always include user input as the first message.
        messages.Add(new AgUiMessage
        {
            Id = $"msg:{session.Id}:user:input",
            Role = "user",
            Content = BuildUserInputMessage(session)
        });

        if (maxAssistantMessages <= 0)
        {
            return new AgUiBootstrapMessages { Messages = messages, ExtraEvents = extra };
        }

        var workerCount = ComputeWorkerCount(session);
        var actors = BuildCognitiveActors(session.Id, workerCount);
        var agentMessages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            actorManager,
            actors,
            new AgUiMessageSnapshotOptions
            {
                ThreadId = session.Id,
                MaxAssistantMessages = maxAssistantMessages,
                ResolveLaneId = (stepId, metadata, defaultLaneId) =>
                    ResolveWorkerLaneId(stepId, defaultLaneId, workerCount)
            },
            ct);

        // Add agent messages to the list
        messages.AddRange(agentMessages);

        return new AgUiBootstrapMessages { Messages = messages, ExtraEvents = extra };
    }

    public static async Task<GraphEvent?> TryBuildGraphSnapshotAsync(
        IGraphStore graphStore,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var snap = await graphStore.GetSnapshotAsync(sessionId, ct);
            if ((snap.Nodes is null || snap.Nodes.Count == 0) && (snap.Edges is null || snap.Edges.Count == 0))
                return null;

            // Build edges lookup: to -> [from]
            var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var e in snap.Edges ?? [])
            {
                if (string.IsNullOrWhiteSpace(e.ToId) || string.IsNullOrWhiteSpace(e.FromId)) continue;
                if (!deps.TryGetValue(e.ToId, out var list))
                {
                    list = [];
                    deps[e.ToId] = list;
                }
                list.Add(e.FromId);
            }

            var axioms = (snap.Nodes ?? [])
                .Where(n => n.Type == NodeType.Axiom)
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n => n.Label ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var assumptions = (snap.Nodes ?? [])
                .Where(n => n.Type == NodeType.Assumption)
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n => new AssumptionNode
                {
                    Id = n.Id ?? "",
                    Statement = n.Label ?? "",
                    Motivation = n.Proof ?? ""
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .ToList();

            var theorems = (snap.Nodes ?? [])
                .Where(n => n.Type == NodeType.Theorem)
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n =>
                {
                    deps.TryGetValue(n.Id, out var d);
                    var dependsOn = d is null
                        ? []
                        : d.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
                    return new TheoremNode
                    {
                        Id = n.Id ?? "",
                        Statement = n.Label ?? "",
                        Proof = n.Proof ?? "",
                        DependsOn = dependsOn
                    };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .ToList();

            var iteration = InferIteration(theorems);

            return new GraphEvent
            {
                SessionId = sessionId,
                Timestamp = DateTimeOffset.UtcNow,
                Iteration = iteration,
                Axioms = axioms,
                Assumptions = assumptions,
                Theorems = theorems
            };
        }
        catch
        {
            return null;
        }
    }

    private static int InferIteration(List<TheoremNode> theorems)
    {
        var max = 0;
        foreach (var t in theorems)
        {
            var id = (t.Id ?? "").Trim();
            if (id.Length < 2) continue;
            if (!id.StartsWith('T')) continue;
            if (!int.TryParse(id[1..], out var n)) continue;
            if (n > max) max = n;
        }

        return max > 0 ? max : theorems.Count;
    }

    private static string BuildUserInputMessage(AxiomSession session)
    {
        var axioms = string.IsNullOrWhiteSpace(session.AxiomsText) ? "(empty)" : session.AxiomsText.Trim();
        var goal = string.IsNullOrWhiteSpace(session.Goal) ? "(none)" : session.Goal.Trim();

        return $"""
               AXIOMS:
               {axioms}

               GOAL / FOCUS:
               {goal}
               """;
    }

    private static int ComputeWorkerCount(AxiomSession session)
    {
        // Must match AxiomReasoningService.BuildReasoningOptions()
        var k = Math.Clamp(session.K, 1, 9);
        return Math.Clamp(2 * k - 1, 1, 15);
    }

    private static List<AgUiActor> BuildCognitiveActors(string sessionId, int workerCount)
    {
        // Stable Actor IDs derived from sessionId (aligned with CognitiveStrategy session-aware AgentId).
        // NOTE: These are runtime IDs, not UI semantics.
        const string agentIdPrefix = "cognitive";

        var stableSessionKey = sessionId;
        var coordinatorRawId = DeterministicGuid
            .FromString($"{agentIdPrefix}:{stableSessionKey}:coordinator")
            .ToString("D");

        var actors = new List<AgUiActor>(capacity: Math.Max(1, workerCount + 1))
        {
            new()
            {
                ActorId = coordinatorRawId,
                ActorTypeName = typeof(CognitiveCoordinatorGAgent).Name,
                LaneId = "coordinator",
                CreateAsync = async (mgr, ct) =>
                    await mgr.CreateAndRegisterAsync<CognitiveCoordinatorGAgent>(coordinatorRawId, ct)
            }
        };

        for (var i = 0; i < workerCount; i++)
        {
            var workerRawId = DeterministicGuid
                .FromString($"{agentIdPrefix}:{stableSessionKey}:worker:{i}")
                .ToString("D");

            var laneId = $"worker-{i}";
            actors.Add(new AgUiActor
            {
                ActorId = workerRawId,
                ActorTypeName = typeof(RoleAIGAgent).Name,
                LaneId = laneId,
                CreateAsync = async (mgr, ct) =>
                {
                    var actor = await mgr.CreateAndRegisterAsync<RoleAIGAgent>(workerRawId, ct);
                    try
                    {
                        if (actor.GetAgent() is RoleAIGAgent worker)
                        {
                            worker.SetStepExecutionHandler(new CognitiveStepExecutionHandler());
                        }
                    }
                    catch (NotSupportedException)
                    {
                        // Best-effort: some runtimes do not expose agent instances.
                    }

                    return actor;
                }
            });
        }

        return actors;
    }

    private static string ResolveWorkerLaneId(string stepId, string defaultLaneId, int workerCount)
    {
        // Only remap when the message originates from the coordinator lane.
        // For worker lanes, keep the lane stable to match live-stream worker grouping.
        if (!string.Equals(defaultLaneId, "coordinator", StringComparison.Ordinal))
            return defaultLaneId;

        if (string.IsNullOrWhiteSpace(stepId) || workerCount <= 0)
            return "coordinator";

        stepId = stepId.Trim();
        var lower = stepId.ToLowerInvariant();

        // Keep coordinator-lane steps in coordinator.
        if (lower.Contains("check_atomic") ||
            lower.Contains("coordinator") ||
            lower.EndsWith(".vote") ||
            (lower.Contains("compose") && !lower.Contains("gen[")))
        {
            return "coordinator";
        }

        // Pattern 1: gen[n] (1-based)
        var gen = Regex.Match(stepId, @"gen\[(\d+)\]");
        if (gen.Success && int.TryParse(gen.Groups[1].Value, out var genIndex))
        {
            var idx = (genIndex - 1) % workerCount;
            if (idx < 0) idx = 0;
            return $"worker-{idx}";
        }

        // Pattern 2: trailing [i] (0-based)
        var tail = Regex.Match(stepId, @"\[(\d+)\]$");
        if (tail.Success && int.TryParse(tail.Groups[1].Value, out var i))
        {
            var idx = i % workerCount;
            if (idx < 0) idx = 0;
            return $"worker-{idx}";
        }

        return "coordinator";
    }
}

