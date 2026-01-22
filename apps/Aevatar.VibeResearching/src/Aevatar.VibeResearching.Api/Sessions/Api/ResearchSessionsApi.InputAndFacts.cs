using Aevatar.Agents.AGUI;
using Aevatar.Agents.Core.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using VibeResearching.Api;
using VibeResearching.Api.Facts;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Sessions;

internal static partial class ResearchSessionsApi
{
    private static void MapInput(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/input", async (
            string sessionId,
            SessionInputInDto input,
            ResearchSessionManager sessions,
            ResearchRunExecutor executor,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            if (string.IsNullOrWhiteSpace(input.Message))
                return Results.BadRequest(new { error = "message is required" });

            // Fire-and-forget run; clients receive progress via AG-UI SSE.
            var runSeq = session.NextRunSeq();
            var runId = $"{session.Id}:{runSeq}";

            _ = Task.Run(async () =>
            {
                // Latest-wins: new message cancels previous run for this session.
                var run = session.BeginNewRun(runId, reason: "new_input", out var interruptedRunId);

                if (!string.IsNullOrWhiteSpace(interruptedRunId))
                {
                    // Record interruption context for the new run to process
                    session.RecordInterruption(new InterruptionContext
                    {
                        InterruptedRunId = interruptedRunId,
                        NewUserMessage = input.Message ?? string.Empty,
                        InterruptedAt = DateTimeOffset.UtcNow,
                        Reason = "new_input",
                        // 从 Workspace.Vibe 读取进度信息
                        TotalMilestones = session.Workspace.Vibe.TotalMilestones,
                        CompletedMilestones = session.Workspace.Vibe.CompletedMilestones,
                        InterruptedAtMilestoneIndex = session.Workspace.Vibe.CurrentMilestoneIndex
                    });

                    // Tell UI immediately (even if the old run was still queued on RunLock).
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Name = "aevatar.scientific.run_interrupted",
                        Value = new
                        {
                            threadId = session.Id,
                            oldRunId = interruptedRunId,
                            newRunId = runId,
                            reason = "new_input"
                        }
                    });

                    // Immediate feedback: acknowledge the user's input
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Name = "aevatar.scientific.system_reply",
                        Value = new
                        {
                            sessionId = session.Id,
                            messageType = "acknowledgment",
                            content = "Got it! Analyzing your request..."
                        }
                    });
                }

                using var scope = RunContextScope.Begin(run);
                try
                {
                    await executor.ExecuteAsync(session, runId, input, run.Token);
                }
                finally
                {
                    // Only clear if still active (avoid clearing a newer run).
                    _ = session.TryClearActiveRun(runId, run);
                    run.Dispose();
                }
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true, sessionId = session.Id, runId });
        });
    }

    private static void MapMcpReconnect(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/mcp/reconnect", (
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            // Fire-and-forget; result is streamed back via SSE (CUSTOM events + tools_snapshot refresh).
            _ = Task.Run(async () =>
            {
                long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.mcp_reconnect_started",
                    Value = new { sessionId = session.Id }
                });

                try
                {
                    var (agent, _) = await runtime.GetAgentAsync(session.Id, session.ProviderName, CancellationToken.None);
                    var (ok, error) = await agent.ReconnectMcpAsync(CancellationToken.None);

                    // Always refresh the tools snapshot so UI updates immediately (even if reconnect failed).
                    var (tools, _mcpNames) = await runtime.RefreshToolsSnapshotAsync(session.Id, session.ProviderName, CancellationToken.None);
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(),
                        Name = "aevatar.scientific.tools_snapshot",
                        Value = new { sessionId = session.Id, tools }
                    });

                    if (ok)
                    {
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = Ts(),
                            Name = "aevatar.scientific.mcp_reconnect_finished",
                            Value = new { sessionId = session.Id, ok = true }
                        });
                    }
                    else
                    {
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = Ts(),
                            Name = "aevatar.scientific.mcp_reconnect_error",
                            Value = new { sessionId = session.Id, ok = false, error = error ?? "mcp reconnect failed" }
                        });
                    }
                }
                catch (Exception ex)
                {
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(),
                        Name = "aevatar.scientific.mcp_reconnect_error",
                        Value = new { sessionId = session.Id, ok = false, error = ex.Message }
                    });
                }
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true });
        });
    }

    private static void MapFacts(WebApplication app)
    {
        // Facts workflow: create a proposal under workspace facts_proposed/ (NOT directly into DAG).
        app.MapPost("/api/sessions/{sessionId}/facts", async (
            string sessionId,
            SaveFactInDto input,
            ResearchSessionManager sessions,
            FactLifecycleService facts,
            IOptions<MaterialsOptions> materialsOptions,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            // Safe-by-default: reuse existing write switch (same as previous facts write-back).
            if (!materialsOptions.Value.AllowWrite)
                return Results.Problem(title: "dag fact write is disabled", detail: "Materials:AllowWrite=false", statusCode: 403);

            var content = (input.Content ?? string.Empty).Trim();
            if (content.Length == 0)
                return Results.BadRequest(new { error = "content is required" });

            try
            {
                var title = (input.Title ?? string.Empty).Trim();
                var maxWrite = Math.Clamp(materialsOptions.Value.MaxWriteChars, 1, 500_000);
                if (content.Length > maxWrite)
                    content = content[..maxWrite];

                var proposal = await facts.CreateProposalAsync(
                    session.Id,
                    title,
                    content,
                    evidencePaths: input.EvidencePaths,
                    proposedBy: "api",
                    ct);

                // Refresh file-backed workspace snapshot for UI.
                WorkspaceProjection.ApplyKnowledge(session.Workspace, workspace.ScanWorkspace(session.Id), null);
                session.Events.Publish(new StateSnapshotEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Snapshot = session.Workspace
                });

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = "aevatar.scientific.fact_proposed",
                    Value = new { sessionId = session.Id, factId = proposal.FactId, title = proposal.Title }
                });

                return Results.Json(new
                {
                    ok = true,
                    sessionId = session.Id,
                    proposal = new { factId = proposal.FactId, title = proposal.Title }
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "fact proposal failed", detail: ex.Message, statusCode: 500);
            }
        });
    }

    private static void MapWorkspace(WebApplication app)
    {
        // Bounded snapshot derived from file-backed workspace.
        app.MapGet("/api/sessions/{sessionId}/workspace", (
            string sessionId,
            ResearchSessionManager sessions,
            WorkspaceService workspace) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var scan = workspace.ScanWorkspace(session.Id);
            return Results.Json(new { ok = true, sessionId = session.Id, workspace = scan });
        });

        app.MapPost("/api/sessions/{sessionId}/facts/{factId}/votes", async (
            string sessionId,
            string factId,
            FactVoteInDto input,
            ResearchSessionManager sessions,
            FactLifecycleService facts,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var reviewerId = (input.ReviewerId ?? string.Empty).Trim();
            var voteRaw = (input.Vote ?? string.Empty).Trim().ToLowerInvariant();

            if (reviewerId.Length == 0)
                return Results.BadRequest(new { error = "reviewerId is required" });

            var vote = voteRaw switch
            {
                "approve" => FactVoteValue.Approve,
                "reject" => FactVoteValue.Reject,
                "needs_work" or "needswork" => FactVoteValue.NeedsWork,
                _ => FactVoteValue.Unspecified
            };

            if (vote == FactVoteValue.Unspecified)
                return Results.BadRequest(new { error = "vote must be approve|reject|needs_work" });

            var msg = new FactVote
            {
                FactId = factId,
                ReviewerId = reviewerId,
                Vote = vote,
                Comment = (input.Comment ?? string.Empty).Trim()
            };

            await facts.RecordVoteAsync(session.Id, msg, ct);

            WorkspaceProjection.ApplyKnowledge(session.Workspace, workspace.ScanWorkspace(session.Id), null);
            session.Events.Publish(new StateSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Snapshot = session.Workspace
            });

            return Results.Json(new { ok = true, sessionId = session.Id, factId });
        });

        app.MapPost("/api/sessions/{sessionId}/facts/{factId}/verifications", async (
            string sessionId,
            string factId,
            FactVerificationInDto input,
            ResearchSessionManager sessions,
            FactLifecycleService facts,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var verifierId = (input.VerifierId ?? string.Empty).Trim();
            if (verifierId.Length == 0)
                return Results.BadRequest(new { error = "verifierId is required" });

            if (input.Result is null)
                return Results.BadRequest(new { error = "result is required" });

            var msg = new FactVerification
            {
                FactId = factId,
                VerifierId = verifierId,
                Tool = (input.Tool ?? "unknown").Trim(),
                Result = input.Result.Value,
                LogExcerpt = (input.LogExcerpt ?? string.Empty).Trim()
            };

            if (input.ArtifactPaths != null)
            {
                foreach (var p in input.ArtifactPaths)
                {
                    var t = (p ?? string.Empty).Trim();
                    if (t.Length == 0) continue;
                    msg.ArtifactPaths.Add(t);
                }
            }

            await facts.RecordVerificationAsync(session.Id, msg, ct);

            WorkspaceProjection.ApplyKnowledge(session.Workspace, workspace.ScanWorkspace(session.Id), null);
            session.Events.Publish(new StateSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Snapshot = session.Workspace
            });

            return Results.Json(new { ok = true, sessionId = session.Id, factId });
        });

        app.MapPost("/api/sessions/{sessionId}/facts/{factId}/promote", async (
            string sessionId,
            string factId,
            PromoteFactInDto? input,
            ResearchSessionManager sessions,
            FactLifecycleService facts,
            WorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var finalizedBy = (input?.FinalizedBy ?? "api").Trim();
            var decision = await facts.EvaluateAndPromoteAsync(session.Id, session.EffectiveDagId, factId, finalizedBy, ct);

            WorkspaceProjection.ApplyKnowledge(session.Workspace, workspace.ScanWorkspace(session.Id), null);
            session.Events.Publish(new StateSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Snapshot = session.Workspace
            });

            if (decision == null)
                return Results.Json(new { ok = true, sessionId = session.Id, factId, pending = true });

            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                factId,
                decision = decision.Decision.ToString(),
                basis = decision.Basis,
                rationale = decision.Rationale
            });
        });
    }
}
