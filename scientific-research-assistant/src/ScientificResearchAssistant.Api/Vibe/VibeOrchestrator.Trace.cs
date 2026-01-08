using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Vibe.Brief;
using ScientificResearchAssistant.Api.Vibe.Delivery;
using ScientificResearchAssistant.Api.Vibe.Dag;
using ScientificResearchAssistant.Api.Vibe.Goals;
using ScientificResearchAssistant.Api.Vibe.Trace;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe;

internal sealed partial class VibeOrchestrator
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
        var prev = await _trace.LoadLatestAsync(sessionId, max: 1, ct);
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

        await _trace.AppendAsync(sessionId, round, summaryMarkdown, ct);

        // Emit a compact event for UI to extend timeline.
        try
        {
            var ws = _workspace.EnsureSessionWorkspace(sessionId);
            var summaryAbs = Path.Combine(ws.RunsDir, runId, "summary.md");
            var summaryRel = Path.GetRelativePath(ws.SessionRoot, summaryAbs).Replace('\\', '/').Trim('/');

            var preview = (summaryMarkdown ?? string.Empty).Replace("\r", "").Trim();
            if (preview.Length > 800) preview = preview[..800];

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
