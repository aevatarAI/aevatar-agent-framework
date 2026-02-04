using System.Text;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.Cognitive.Researching.Paper;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingRoundServices
{
    // ============================================================
    //  AG-UI per-agent message projection (like AxiomReasoning)
    // ============================================================

    private static void StartAgentMessage(ResearchSession session, string messageId, string agent, string stepName, string? providerName)
    {
        // Ensure message exists for snapshot-first reconnect.
        session.SetMessage(messageId, role: "assistant", content: string.Empty);

        session.Events.Publish(new TextMessageStartEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId,
            Role = "assistant"
        });

        // Attach metadata so frontend can label/group per agent without parsing messageId.
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.message_meta",
            Value = new { messageId, agent, stepName, providerName = (providerName ?? string.Empty).Trim() }
        });
    }

    private static void EmitAgentDelta(ResearchSession session, string messageId, string role, string delta)
    {
        if (string.IsNullOrEmpty(delta))
            return;

        session.AppendToMessage(messageId, role: role, delta);

        session.Events.Publish(new TextMessageContentEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId,
            Delta = delta
        });
    }

    private static void EndAgentMessage(ResearchSession session, string messageId)
    {
        session.Events.Publish(new TextMessageEndEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId
        });
    }

    // ============================================================
    //  Agent Status Reporting
    // ============================================================

    /// <summary>
    /// Emit an agent status report event for real-time UI updates.
    /// Status text should be a brief, human-readable description of current work.
    /// </summary>
    private static void EmitAgentStatusReport(
        ResearchSession session,
        string agentName,
        string statusText,
        double? progress = null)
    {
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = VibeEventNames.AgentStatusReport,
            Value = new
            {
                agentId = agentName,
                agentName,
                statusText,
                progress,
                sessionId = session.Id
            }
        });
    }

    // Agent-specific status messages
    private static class AgentStatusMessages
    {
        public const string PaperEditorStart = "正在更新论文草稿...";
    }

    // ============================================================
    //  Paper editor worker
    // ============================================================

    private async Task<string> RunPaperEditorAsync(
        ResearchingRoundContext ctx,
        DagRoundResult dagResult,
        IReadOnlyDictionary<string, string> outputs,
        string? providerName,
        CancellationToken ct)
    {
        var session = ctx.Session;
        EmitAgentStatusReport(session, "paper_editor", AgentStatusMessages.PaperEditorStart);
        var messageId = $"msg:{session.Id}:paper_editor:{ctx.RunId}";
        StartAgentMessage(session, messageId, agent: "paper_editor", stepName: "vibe.paper_editor", providerName: providerName);

        try
        {
            // Ensure paper scaffold exists; we will include bounded excerpts for context.
            var ws = await _core.Paper.EnsurePaperFilesAsync(session.Id, ct);
            var outline = await SafeReadTextAsync(ws.PaperOutlinePath, maxChars: 8000, ct);
            var draft = await SafeReadTextAsync(ws.PaperDraftPath, maxChars: 12_000, ct);

            var (pe, peId) = await _core.Runtime.GetPaperEditorAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = BuildPaperEditorMessage(ctx.Question, dagResult, outputs, outline, draft, ctx.Input.AttachmentPaths),
                RequestId = ctx.Input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:paper_editor"
            };
            req.Context["agent_id"] = peId;
            req.Context["materials_context"] = ctx.Materials.RenderedContext;

            var sb = new StringBuilder(4096);
            var supportsStreaming = await pe.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await pe.ChatAsync(req, ct);
                var text = (resp.Content ?? string.Empty).Trim();
                sb.Append(text);
                if (text.Length > 0)
                    EmitAgentDelta(session, messageId, "assistant", Bound(text, 30_000) + "\n\n");
            }
            else
            {
                await foreach (var chunk in pe.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                    if (sb.Length > 40_000) break;
                }
                EmitAgentDelta(session, messageId, "assistant", "\n\n");
            }

            return Bound(sb.ToString().Trim(), 40_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[paper_editor error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

}
