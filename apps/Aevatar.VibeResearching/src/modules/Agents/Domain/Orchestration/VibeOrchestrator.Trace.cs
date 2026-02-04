using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Agents.Mesh;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Orchestration;

namespace Aevatar.VibeResearching.Agents;

public sealed partial class VibeOrchestrator
{
    // ============================================================
    //  Trace persistence + round summary event
    // ============================================================

    private async Task PersistTraceAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        IReadOnlyDictionary<string, string> outputs,
        DagRoundResult dagResult,
        string? summaryMarkdown,
        CancellationToken ct)
    {
        var sessionId = session.Id;
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        // Best-effort monotonic round index
        var prev = await _core.Trace.LoadLatestAsync(sessionId, max: 1, ct);
        var roundIdx = prev.Count == 0 ? 0 : prev[^1].RoundIndex + 1;

        var round = new SraRoundSummary
        {
            SessionId = sessionId,
            RunId = runId,
            RoundIndex = roundIdx,
            TriggerKind = "user_message",
            TriggerRef = (input.RequestId ?? string.Empty).Trim(),
            UpdatedAt = now
        };

        foreach (var (agent, text) in outputs)
        {
            var s = new SraRoundAgentSummary { Agent = agent };
            s.Highlights.AddRange(ExtractHighlights(text, max: 4));
            round.PerAgent.Add(s);
        }

        if (dagResult.Accepted && dagResult.AcceptedMutation != null)
        {
            foreach (var n in dagResult.AcceptedMutation.UpsertNodes)
            {
                round.DagChanges.Add(new SraRoundDagChange
                {
                    NodeId = n.Id,
                    NodeType = n.Type,
                    Change = "accepted"
                });
            }
        }
        else if (dagResult.Blocked && dagResult.StagedPath != null && dagResult.Candidate != null)
        {
            foreach (var n in dagResult.Candidate.UpsertNodes)
            {
                round.DagChanges.Add(new SraRoundDagChange
                {
                    NodeId = n.Id,
                    NodeType = n.Type,
                    Change = "staged"
                });
            }
        }

        round.Metrics["question_len"] = question.Length.ToString();
        round.Metrics["agents"] = outputs.Count.ToString();

        await _core.Trace.AppendAsync(sessionId, round, summaryMarkdown, ct);

        // Emit a compact event for UI to extend timeline.
        try
        {
            var ws = _core.Workspace.EnsureSessionWorkspace(sessionId);
            var summaryAbs = Path.Combine(ws.RunsDir, runId, "summary.md");
            var summaryRel = Path.GetRelativePath(ws.SessionRoot, summaryAbs).Replace('\\', '/').Trim('/');

            // NOTE:
            // - UI wants the full summary text here (no truncation).
            // - This may increase SSE payload size, but trace summaries are already persisted to file;
            //   the UI still has max-height/scroll to stay usable.
            var preview = (summaryMarkdown ?? string.Empty).Replace("\r", "").Trim();

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.round_summary",
                Value = new
                {
                    sessionId,
                    runId,
                    roundIndex = roundIdx,
                    summaryPath = summaryRel,
                    preview
                }
            });
        }
        catch
        {
            // best-effort only
        }
    }


}
