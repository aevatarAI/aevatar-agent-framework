using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    private sealed class PlanEditJson
    {
        public List<PlanEditMilestoneJson>? Milestones { get; init; }
        public string? Note { get; init; }
    }

    private sealed class PlanEditMilestoneJson
    {
        public int? RoundIndex { get; init; }
        public string? ExpectedOutput { get; init; }
    }

    internal async Task<(SraDagSnapshot? Dag, string Note)> EditDagPlanMilestonesAsync(
        ResearchSession session,
        string dagId,
        string instruction,
        string? providerOverride,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        dagId = (dagId ?? string.Empty).Trim();
        instruction = (instruction ?? string.Empty).Replace("\r", "").Trim();
        if (dagId.Length == 0 || instruction.Length == 0)
            return (null, "missing dagId or instruction");

        try
        {
            // Load current plan context from DAG (milestones + current round plan).
            var cur = await _dag.LoadSnapshotAsync(dagId, ct);

            static bool IsDeleted(SraDagNode n)
            {
                if (n.Tags == null) return false;
                if (!n.Tags.TryGetValue("deleted", out var v)) return false;
                return string.Equals((v ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }

            bool IsMilestone(SraDagNode n) =>
                !IsDeleted(n) &&
                n.Tags != null &&
                n.Tags.TryGetValue("planKind", out var v) &&
                string.Equals((v ?? string.Empty).Trim(), "milestone", StringComparison.OrdinalIgnoreCase);

            var existingMilestones = cur.Nodes
                .Where(n => n != null && n.Kind == SraDagNodeKind.Plan && IsMilestone(n))
                .OrderBy(n =>
                {
                    n!.Tags.TryGetValue("milestoneRoundIndex", out var s);
                    return int.TryParse((s ?? string.Empty).Trim(), out var x) && x > 0 ? x : int.MaxValue;
                })
                .ThenBy(n => n!.Id, StringComparer.Ordinal)
                .Take(20)
                .ToList();

            var sbCtx = new StringBuilder(1024);
            sbCtx.AppendLine("Current Plan (milestones in DAG):");
            if (existingMilestones.Count == 0)
            {
                sbCtx.AppendLine("- (none)");
            }
            else
            {
                foreach (var p in existingMilestones)
                {
                    p!.Tags.TryGetValue("milestoneRoundIndex", out var s);
                    var idx = (s ?? string.Empty).Trim();
                    var label = (p.Label ?? string.Empty).Replace("\r", "").Trim();
                    sbCtx.Append("- ").Append(string.IsNullOrWhiteSpace(idx) ? "N" : idx).Append(": ")
                        .AppendLine(Bound(label, 220));
                }
            }

            // Ask research_assistant to output a revised milestones list.
            var (ra, raId) = await _runtime.GetResearchAssistantAgentAsync(session.Id, providerOverride, ct);
            var req = new ChatRequest
            {
                Message =
                    "[MODE:PLAN_EDIT]\n" +
                    sbCtx.ToString().TrimEnd() +
                    "\n\nUser instruction:\n" +
                    instruction +
                    "\n\nReturn JSON ONLY in this schema:\n" +
                    "{ \"milestones\": [ { \"roundIndex\": 1, \"expectedOutput\": \"...\" } ], \"note\": \"...\" }\n",
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:plan_edit"
            };
            req.Context["agent_id"] = raId;

            var resp = await ra.ChatAsync(req, ct);
            var raw = (resp.Content ?? string.Empty).Trim();
            if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
                return (null, "failed to parse plan_edit JSON");

            PlanEditJson? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<PlanEditJson>(json!, Json);
            }
            catch
            {
                return (null, "invalid plan_edit JSON");
            }

            var items = parsed?.Milestones?
                .Select(m => new
                {
                    round = Math.Clamp(m?.RoundIndex ?? 0, 0, 200),
                    expected = (m?.ExpectedOutput ?? string.Empty).Replace("\r", "").Trim()
                })
                .Where(x => x.expected.Length > 0)
                .Take(12)
                .ToList() ?? [];

            if (items.Count == 0)
                return (null, "no milestones returned");

            // Upsert milestones into DAG using stable ids; mark removed ones as deleted.
            var now = Timestamp.FromDateTime(DateTime.UtcNow);
            var mutationId = $"plan_edit_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            var m = new SraDagMutation
            {
                SessionId = session.Id,
                MutationId = mutationId,
                AuthorAgent = "research_assistant",
                CreatedAt = now
            };
            m.Labels["kind"] = "plan";
            m.Labels["planKind"] = "milestone";
            m.Labels["source"] = "user_chat";

            var desiredIds = new HashSet<string>(StringComparer.Ordinal);
            var i = 0;
            foreach (var it in items)
            {
                i++;
                var suffix = it.round > 0 ? $"r{it.round}" : $"i{i}";
                var nodeId = SanitizeId($"plan_{session.Id}_ms_{suffix}");
                if (nodeId.Length == 0) nodeId = $"plan_{Guid.NewGuid():N}";
                desiredIds.Add(nodeId);

                var label = it.round > 0
                    ? $"Milestone (Round {it.round}): {Bound(it.expected, 160)}"
                    : $"Milestone: {Bound(it.expected, 180)}";

                var proof = $"UserEdit:\n{Bound(instruction, 900)}\n\nExpectedOutput:\n{Bound(it.expected, 900)}";

                var node = new SraDagNode
                {
                    Id = nodeId,
                    Type = SraDagNodeType.Assumption,
                    Label = Bound(label, 200),
                    Proof = Bound(proof, 1200),
                    UpdatedAt = now,
                    Kind = SraDagNodeKind.Plan
                };
                node.Tags["originSessionId"] = session.Id;
                node.Tags["author"] = "research_assistant";
                node.Tags["planKind"] = "milestone";
                node.Tags["milestoneRoundIndex"] = it.round.ToString();
                node.Tags["deleted"] = "false";
                m.UpsertNodes.Add(node);
            }

            // Mark removed milestone nodes (for this session) as deleted=true so they stop showing up.
            foreach (var old in existingMilestones)
            {
                var id = (old?.Id ?? string.Empty).Trim();
                if (id.Length == 0) continue;
                if (desiredIds.Contains(id)) continue;
                old!.Tags["deleted"] = "true";
                old.UpdatedAt = now;
                m.UpsertNodes.Add(old);
            }

            var applied = await _dag.ApplyMutationAsync(dagId, m, ct);

            // Notify UI to refresh dag snapshot (same pattern as other dag writes).
            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.dag_updated",
                Value = new { sessionId = session.Id, dagId, mutationId }
            });

            var note = (parsed?.Note ?? string.Empty).Replace("\r", "").Trim();
            if (note.Length == 0) note = "plan milestones updated";
            return (applied, note);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            return (null, ex.Message);
        }
    }
}