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
using ScientificResearchAssistant.Api.Vibe.Trace;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe;

internal sealed partial class VibeOrchestrator
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
    //  Workers
    // ============================================================

    private async Task<string> RunPlannerAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        string? providerName,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
        var messageId = $"msg:{session.Id}:planner:{runId}";
        StartAgentMessage(session, messageId, agent: "planner", stepName: "vibe.planner", providerName: providerName);

        try
        {
            var (planner, plannerId) = await _core.Runtime.GetPlannerAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = BuildWorkerMessage("planner", question, dag, attachments: input.AttachmentPaths),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:planner"
            };
            req.Context["agent_id"] = plannerId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(1024);
            await foreach (var chunk in planner.ChatStreamAsync(req, ct))
            {
                if (string.IsNullOrEmpty(chunk)) continue;
                sb.Append(chunk);
                EmitAgentDelta(session, messageId, "assistant", chunk);
            }

            EmitAgentDelta(session, messageId, "assistant", "\n\n");
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
            return Bound(sb.ToString(), 20_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[planner error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunReasonerAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        string? plannerOutput,
        string? providerName,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
        var messageId = $"msg:{session.Id}:reasoner:{runId}";
        StartAgentMessage(session, messageId, agent: "reasoner", stepName: "vibe.reasoner", providerName: providerName);

        try
        {
            var (reasoner, reasonerId) = await _core.Runtime.GetReasonerAgentAsync(session.Id, providerName, ct);

            // Best-effort: include python tool if enabled.
            _ = await _core.Runtime.RefreshToolsSnapshotAsync(session.Id, providerName, ct);

            var req = new ChatRequest
            {
                Message = BuildWorkerMessage("reasoner", question, dag, attachments: input.AttachmentPaths,
                    extra: string.IsNullOrWhiteSpace(plannerOutput) ? null : $"Planner output (excerpt):\n{Bound(plannerOutput!, 3000)}"),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:reasoner"
            };
            req.Context["agent_id"] = reasonerId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(2048);
            var supportsStreaming = await reasoner.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await reasoner.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in reasoner.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }

            EmitAgentDelta(session, messageId, "assistant", "\n\n");
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
            return Bound(sb.ToString(), 40_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[reasoner error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunLibrarianAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        string? providerName,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
        var messageId = $"msg:{session.Id}:librarian:{runId}";
        StartAgentMessage(session, messageId, agent: "librarian", stepName: "vibe.librarian", providerName: providerName);

        try
        {
            var (lib, libId) = await _core.Runtime.GetLibrarianAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = BuildWorkerMessage("librarian", question, dag, attachments: input.AttachmentPaths),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:librarian"
            };
            req.Context["agent_id"] = libId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(1024);
            var supportsStreaming = await lib.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await lib.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in lib.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }
            EmitAgentDelta(session, messageId, "assistant", "\n\n");

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
            return Bound(sb.ToString(), 20_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[librarian error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunVerifierAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerName,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
        var messageId = $"msg:{session.Id}:verifier:{runId}";
        StartAgentMessage(session, messageId, agent: "verifier", stepName: "vibe.verifier", providerName: providerName);

        try
        {
            var (ver, verId) = await _core.Runtime.GetVerifierAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = BuildWorkerMessage("verifier", question, dag, attachments: input.AttachmentPaths,
                    extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}"),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:verifier"
            };
            req.Context["agent_id"] = verId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(1024);
            var supportsStreaming = await ver.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await ver.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in ver.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }
            EmitAgentDelta(session, messageId, "assistant", "\n\n");

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
            return Bound(sb.ToString(), 20_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[verifier error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunDagBuilderAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<LibrarianAxiomCandidate> librarianAxioms,
        string? providerName,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
        var messageId = $"msg:{session.Id}:dag_builder:{runId}";
        StartAgentMessage(session, messageId, agent: "dag_builder", stepName: "vibe.dag_builder", providerName: providerName);

        try
        {
            var (db, dbId) = await _core.Runtime.GetDagBuilderAgentAsync(session.Id, providerName, ct);
            var req = new ChatRequest
            {
                Message = BuildDagBuilderMessage(question, dag, outputs, librarianAxioms, input.AttachmentPaths),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:dag_builder"
            };
            req.Context["agent_id"] = dbId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(4096);
            var supportsStreaming = await db.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await db.ChatAsync(req, ct);
                var text = (resp.Content ?? string.Empty).Trim();
                sb.Append(text);

                // Render as fenced json to UI (even if it's invalid; user can see it).
                if (text.Length > 0)
                {
                    EmitAgentDelta(session, messageId, "assistant", "```json\n");
                    EmitAgentDelta(session, messageId, "assistant", Bound(text, 20_000) + "\n");
                    EmitAgentDelta(session, messageId, "assistant", "```\n\n");
                }
            }
            else
            {
                // Stream inside fenced block for a better "always streaming" UX.
                EmitAgentDelta(session, messageId, "assistant", "```json\n");

                await foreach (var chunk in db.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);

                    // Keep bounded (same as return contract).
                    if (sb.Length > 30_000)
                        break;
                }

                EmitAgentDelta(session, messageId, "assistant", "\n```\n\n");
            }

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
            return Bound(sb.ToString().Trim(), 30_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = $"[dag_builder error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunPaperEditorAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        DagRoundResult dagResult,
        IReadOnlyDictionary<string, string> outputs,
        string? providerName,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.paper_editor" });
        var messageId = $"msg:{session.Id}:paper_editor:{runId}";
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
                Message = BuildPaperEditorMessage(question, dagResult, outputs, outline, draft, input.AttachmentPaths),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:paper_editor"
            };
            req.Context["agent_id"] = peId;
            req.Context["materials_context"] = materials.RenderedContext;

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

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.paper_editor" });
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
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.paper_editor" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

}
