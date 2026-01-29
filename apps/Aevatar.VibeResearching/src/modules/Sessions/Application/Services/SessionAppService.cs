using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Core.Runtime;
using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;
using Aevatar.VibeResearching.Agents.Orchestration;
using Aevatar.VibeResearching.Sessions.Permissions;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Sessions.DTOs;
using Aevatar.VibeResearching.Sessions.MongoDB.Services;

namespace Aevatar.VibeResearching.Sessions.Application;

/// <summary>
/// Application service for managing research sessions.
/// Delegates to ResearchSessionManager and session repositories.
/// Wires ResearchRunExecutor for input processing.
/// </summary>
public class SessionAppService : ApplicationService, ISessionAppService
{
    private readonly IVibeSessionRepository _sessionRepository;
    private readonly ResearchSessionManager _sessionManager;
    private readonly ResearchRunExecutor _runExecutor;
    private readonly SessionUiSnapshotStore _uiSnapshotStore;

    public SessionAppService(
        IVibeSessionRepository sessionRepository,
        ResearchSessionManager sessionManager,
        ResearchRunExecutor runExecutor,
        SessionUiSnapshotStore uiSnapshotStore)
    {
        _sessionRepository = sessionRepository;
        _sessionManager = sessionManager;
        _runExecutor = runExecutor;
        _uiSnapshotStore = uiSnapshotStore;
    }

    /// <inheritdoc/>
    public async Task<VibeSessionRecord> CreateAsync(CreateSessionDto input, CancellationToken ct = default)
    {
        // Set OwnerId to current user's ID (null if anonymous/unauthenticated)
        var ownerId = CurrentUser.Id?.ToString();
        var session = await _sessionManager.CreateSessionAsync(input.ProviderName, ownerId, ct);
        return session;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VibeSessionRecord>> GetListAsync(CancellationToken ct = default)
    {
        var all = await _sessionRepository.ListAsync(ct);
        // Exclude archived sessions by default
        return all.Where(s => s.Status != Agents.Contracts.Sessions.SessionStatus.Archived).ToList();
    }

    /// <inheritdoc/>
    public async Task<VibeSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        return await _sessionRepository.GetAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        // Delete requires Sessions.Delete permission (admin only, enforced by controller [Authorize])
        // Additional owner check: admins can delete any, but this is already gated by permission
        await _sessionManager.DeleteSessionAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<string> SubmitInputAsync(string sessionId, SessionInputDto input, CancellationToken ct = default)
    {
        if (!_sessionManager.TryGet(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session '{sessionId}' not found");
        }

        // Owner check: only the session owner or admin can submit input
        await CheckSessionOwnershipAsync(session);

        // Guard: reject input on paused/archived sessions
        session.EnsureAcceptsInput();

        if (string.IsNullOrWhiteSpace(input.Message))
        {
            throw new ArgumentException("Message is required", nameof(input));
        }

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
                // Map SessionInputDto to SessionInputInDto
                var executorInput = MapToExecutorInput(input);
                await _runExecutor.ExecuteAsync(session, runId, executorInput, run.Token);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error executing run {RunId} for session {SessionId}", runId, sessionId);
            }
            finally
            {
                // Only clear if still active (avoid clearing a newer run).
                _ = session.TryClearActiveRun(runId, run);
                run.Dispose();
            }
        }, CancellationToken.None);

        Logger.LogInformation("Started input processing for session {SessionId}, run {RunId}", sessionId, runId);
        return runId;
    }

    /// <summary>
    /// Maps SessionInputDto (from Application.Contracts) to SessionInputInDto (used by ResearchRunExecutor).
    /// </summary>
    private static SessionInputInDto MapToExecutorInput(SessionInputDto input)
    {
        var executorInput = new SessionInputInDto
        {
            RequestId = input.RequestId,
            Message = input.Message,
            Mode = input.Mode,
            ProviderName = input.ProviderName
        };

        if (input.AttachmentPaths != null)
        {
            executorInput.AttachmentPaths = new List<string>(input.AttachmentPaths);
        }

        if (input.ToAgents != null)
        {
            foreach (var agentName in input.ToAgents)
            {
                if (!string.IsNullOrWhiteSpace(agentName))
                {
                    executorInput.ToAgents[agentName] = true;
                }
            }
        }

        if (input.Loop != null)
        {
            executorInput.Loop = new SessionInputInDto.LoopConfig
            {
                MaxIterations = input.Loop.MaxIterations ?? 10,
                MaxTotalDurationMs = input.Loop.MaxTotalDurationMs ?? 3600000
            };
        }

        return executorInput;
    }

    /// <inheritdoc/>
    public async Task ReconnectMcpAsync(string sessionId, CancellationToken ct = default)
    {
        // Owner check: only the session owner or admin can reconnect MCP
        if (_sessionManager.TryGet(sessionId, out var session))
        {
            await CheckSessionOwnershipAsync(session);
        }

        await _sessionManager.ReconnectMcpAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<object> GetToolsAsync(string sessionId, CancellationToken ct = default)
    {
        // Get tools snapshot from session manager
        return await _sessionManager.GetToolsSnapshotAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<object?> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessionManager.TryGet(sessionId, out var session))
        {
            return null;
        }

        var activeRun = session.ActiveRun;
        var workspace = session.Workspace;

        // Load UI snapshot for step/tool/agent data
        var uiSnap = await _uiSnapshotStore.LoadAsync(sessionId, ct);

        // Build steps object matching frontend SessionStatus.steps shape
        object stepsObj;
        if (uiSnap.RunSteps != null && uiSnap.RunSteps.Order is { Count: > 0 })
        {
            var runSteps = uiSnap.RunSteps;
            var running = runSteps.Map
                .Where(kv => kv.Value.Status == "running")
                .Select(kv => kv.Key)
                .ToList();
            var done = runSteps.Map
                .Where(kv => kv.Value.Status == "done")
                .Select(kv => kv.Key)
                .ToList();

            stepsObj = new
            {
                order = runSteps.Order,
                map = runSteps.Map.ToDictionary(
                    kv => kv.Key,
                    kv => new
                    {
                        status = kv.Value.Status,
                        startedAt = kv.Value.StartedAt.HasValue
                            ? DateTimeOffset.FromUnixTimeMilliseconds(kv.Value.StartedAt.Value).ToString("O")
                            : (string?)null,
                        finishedAt = kv.Value.FinishedAt.HasValue
                            ? DateTimeOffset.FromUnixTimeMilliseconds(kv.Value.FinishedAt.Value).ToString("O")
                            : (string?)null
                    }),
                running,
                done
            };
        }
        else
        {
            stepsObj = new { order = Array.Empty<string>(), map = new Dictionary<string, object>(), running = Array.Empty<string>(), done = Array.Empty<string>() };
        }

        // Build agents from message meta
        var agentStepMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var agentProviderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in uiSnap.MessageMeta)
        {
            if (!string.IsNullOrWhiteSpace(meta.Agent))
            {
                agentStepMap[meta.Agent] = meta.StepName ?? "";
                agentProviderMap[meta.Agent] = meta.ProviderName ?? "";
            }
        }

        var agents = agentStepMap.Select(kv => new
        {
            agent = kv.Key,
            stepName = kv.Value,
            providerName = agentProviderMap.GetValueOrDefault(kv.Key, ""),
            status = "idle"
        }).ToList();

        // Build running tools
        var runningTools = uiSnap.Tools
            .Where(t => t.Status == "running")
            .Select(t => new
            {
                messageId = t.MessageId ?? "",
                toolCallId = t.ToolCallId ?? "",
                toolName = t.ToolName ?? "",
                status = t.Status ?? "running",
                startedAt = t.StartedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(t.StartedAt.Value).ToString("O")
                    : "",
                providerName = t.ProviderName ?? "",
                targetAgent = t.TargetAgent ?? ""
            }).ToList();

        return new
        {
            ok = true,
            sessionId = session.Id,
            runId = activeRun?.RunId ?? string.Empty,
            isActive = activeRun != null,
            steps = stepsObj,
            agents,
            runningTools,
            workspace = new
            {
                factsCount = workspace.Knowledge.FactsCount,
                factsProposedCount = workspace.Knowledge.FactsProposedCount,
                materialsCount = workspace.Materials.Items.Count
            },
            updatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    /// <inheritdoc/>
    public async Task PauseAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessionManager.TryGet(sessionId, out var session))
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        await CheckSessionOwnershipAsync(session);
        await _sessionManager.PauseSessionAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task ResumeAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessionManager.TryGet(sessionId, out var session))
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        await CheckSessionOwnershipAsync(session);
        await _sessionManager.ResumeSessionAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task TerminateAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessionManager.TryGet(sessionId, out var session))
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        await CheckSessionOwnershipAsync(session);
        await _sessionManager.TerminateSessionAsync(sessionId, ct);
    }

    /// <summary>
    /// Checks that the current user is the session owner or has admin privileges.
    /// Throws <see cref="AbpAuthorizationException"/> if the user is not authorized.
    /// Sessions with no owner (legacy/anonymous) are accessible by any authenticated user.
    /// </summary>
    private async Task CheckSessionOwnershipAsync(ResearchSession session)
    {
        // Legacy sessions without an owner are accessible by any authenticated user
        if (string.IsNullOrWhiteSpace(session.OwnerId))
            return;

        // Check if current user is the owner
        var currentUserId = CurrentUser.Id?.ToString();
        if (!string.IsNullOrWhiteSpace(currentUserId) &&
            string.Equals(currentUserId, session.OwnerId, StringComparison.OrdinalIgnoreCase))
            return;

        // Not the owner — check if admin (Sessions.Admin bypasses owner check).
        // CheckPolicyAsync throws AbpAuthorizationException if not granted.
        await CheckPolicyAsync(SessionsPermissions.Sessions.Admin);
    }
}
