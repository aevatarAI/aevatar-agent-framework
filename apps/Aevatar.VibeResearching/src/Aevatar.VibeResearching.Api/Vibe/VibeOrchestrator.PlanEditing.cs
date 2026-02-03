using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe;

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
            var cur = await _core.Dag.LoadSnapshotAsync(dagId, ct);

            var existingMilestones = PlanDagMutationBuilder.LoadExistingMilestones(cur)
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
            var (ra, raId) = await _core.Runtime.GetResearchAssistantAgentAsync(session.Id, providerOverride, ct);
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
            if (!VibeWorkflowParsing.TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
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
                .Select(m =>
                {
                    var round = Math.Clamp(m?.RoundIndex ?? 0, 0, 200);
                    var expected = (m?.ExpectedOutput ?? string.Empty).Replace("\r", "").Trim();
                    return new PlanDagMutationBuilder.PlanMilestoneItem(round, expected);
                })
                .Where(x => x.ExpectedOutput.Length > 0)
                .Take(12)
                .ToList() ?? [];

            if (items.Count == 0)
                return (null, "no milestones returned");

            var m = PlanDagMutationBuilder.BuildMilestoneMutation(
                sessionId: session.Id,
                author: "research_assistant",
                source: "user_chat",
                items: items,
                currentDag: cur,
                proofHeader: "UserEdit",
                proofSource: instruction,
                mutationIdPrefix: "plan_edit",
                existingMilestones: existingMilestones,
                markRemoved: true);

            var applied = await _core.Dag.ApplyMutationAsync(dagId, m, ct);

            // Notify UI to refresh dag snapshot (same pattern as other dag writes).
            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.dag_updated",
                Value = new { sessionId = session.Id, dagId, mutationId = m.MutationId ?? string.Empty }
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