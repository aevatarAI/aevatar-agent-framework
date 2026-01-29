using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions.Runtime;

// ============================================================
//  SessionRuntime - 极简版
//
//  职责单一：
//  - Session CRUD
//  - Chat 调度
//  - AG-UI stream 绑定
//
//  简化的状态通知：
//  - SESSION_STATUS 仅保留关键阶段
//  - Message history 管理（由 agent 自己管理）
// ============================================================

public sealed record SessionSummary(
    string SessionId,
    string WorkflowName,
    string PrimaryRole,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SessionPrimaryAgentInfo(
    string SessionId,
    string AgentId,
    string Role,
    string NodeId);

public sealed class SessionRuntime
{
    private readonly CognitiveSessionService _sessions;
    private readonly IGAgentActorManager _actorManager;
    private readonly IAgentMessageStreamResolver _streamResolver;
    private readonly IOptionsMonitor<SessionRuntimeOptions> _options;
    private readonly IWorkflowCatalog _workflowCatalog;
    private readonly AgentBootstrapper _bootstrapper;
    private readonly SessionWorkflowRunner _workflowRunner;
    private readonly IMemoryStore? _memoryStore;
    private readonly ILogger<SessionRuntime> _logger;

    private readonly ConcurrentDictionary<string, SessionContext> _contexts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SessionState> _createdSessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);

    public SessionRuntime(
        CognitiveSessionService sessions,
        IGAgentActorManager actorManager,
        IAgentMessageStreamResolver streamResolver,
        IOptionsMonitor<SessionRuntimeOptions> options,
        IWorkflowCatalog workflowCatalog,
        AgentBootstrapper bootstrapper,
        SessionWorkflowRunner workflowRunner,
        IEnumerable<IMemoryStore> memoryStores,
        ILogger<SessionRuntime> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
        _streamResolver = streamResolver ?? throw new ArgumentNullException(nameof(streamResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _workflowCatalog = workflowCatalog ?? throw new ArgumentNullException(nameof(workflowCatalog));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _workflowRunner = workflowRunner ?? throw new ArgumentNullException(nameof(workflowRunner));
        _memoryStore = memoryStores?.FirstOrDefault();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ============================================================
    //  Session CRUD
    // ============================================================

    public Task<SessionState> CreateSessionAsync(string? workflowName, CancellationToken ct)
        => CreateSessionAsync(workflowName, sessionId: null, ct);

    public async Task<SessionState> CreateSessionAsync(string? workflowName, string? sessionId, CancellationToken ct)
    {
        var name = _workflowCatalog.ResolveWorkflowName(workflowName);
        var request = new StartSessionRequest
        {
            WorkflowName = name,
            SessionId = sessionId ?? string.Empty
        };

        var state = await _sessions.StartSessionAsync(request, ct);
        _createdSessions[state.SessionId] = state;
        _ = await GetOrCreateContextAsync(state, ct);
        return state;
    }

    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(int limit, CancellationToken ct)
    {
        await CleanupIdleContextsAsync(ct: ct);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (_memoryStore != null)
        {
            var response = await _sessions.ListSessionsAsync(limit, ct);
            if (response != null)
            {
                foreach (var resource in response.Resources)
                {
                    var id = resource.Scope?.ScopeId ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(id))
                        ids.Add(id);
                }
            }
        }

        foreach (var id in _createdSessions.Keys)
            ids.Add(id);

        var sessions = new List<SessionSummary>();
        foreach (var id in ids)
        {
            var state = await _sessions.GetSessionStateAsync(id, ct);
            if (state == null)
                continue;

            var role = await GetPrimaryRoleAsync(state, ct);
            sessions.Add(new SessionSummary(
                SessionId: state.SessionId,
                WorkflowName: state.WorkflowName ?? string.Empty,
                PrimaryRole: role?.Role ?? string.Empty,
                CreatedAt: ToDateTimeOffset(state.CreatedAt),
                UpdatedAt: ToDateTimeOffset(state.UpdatedAt)));
        }

        return sessions
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .ToList();
    }

    public async Task CleanupIdleContextsAsync(TimeSpan? idleTimeout = null, CancellationToken ct = default)
    {
        if (_contexts.IsEmpty)
            return;

        if (!await _cleanupGate.WaitAsync(TimeSpan.FromSeconds(1), ct))
            return;

        try
        {
            var timeout = ResolveIdleTimeout(idleTimeout);
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _contexts)
            {
                var ctx = entry.Value;
                if (now - ctx.UpdatedAt < timeout)
                    continue;

                if (_contexts.TryRemove(entry.Key, out var removed))
                    await removed.DisposeAsync();
            }
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    // ============================================================
    //  Agent 查询
    // ============================================================

    public async Task<SessionPrimaryAgentInfo?> ResolvePrimaryAgentAsync(string sessionId, CancellationToken ct)
    {
        var state = await _sessions.GetSessionStateAsync(sessionId, ct);
        if (state == null)
            return null;

        var role = await GetPrimaryRoleAsync(state, ct);
        if (role == null)
            return null;

        return new SessionPrimaryAgentInfo(
            SessionId: state.SessionId,
            AgentId: role.AgentId ?? string.Empty,
            Role: role.Role ?? string.Empty,
            NodeId: role.NodeId ?? string.Empty);
    }

    public async Task<AIGAgentBase?> GetPrimaryAgentAsync(string sessionId, CancellationToken ct)
    {
        var info = await ResolvePrimaryAgentAsync(sessionId, ct);
        if (info == null || string.IsNullOrWhiteSpace(info.AgentId))
            return null;

        var actor = await _actorManager.GetActorAsync(info.AgentId);
        if (actor == null)
            return null;

        try
        {
            var agent = actor.GetAgent() as AIGAgentBase;
            if (agent == null)
                return null;
            _bootstrapper.ConfigureAgentDefaults(info.AgentId, agent, _memoryStore != null);
            return agent;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    // ============================================================
    //  SSE Stream
    // ============================================================

    public async Task<SessionAgUiStream?> GetSessionStreamAsync(string sessionId, CancellationToken ct)
    {
        var state = await _sessions.GetSessionStateAsync(sessionId, ct);
        if (state == null)
            return null;

        var ctx = await GetOrCreateContextAsync(state, ct);
        return ctx.Stream;
    }

    // ============================================================
    //  Chat 调度 - 极简版
    // ============================================================

    public async Task<string> SendChatAsync(string sessionId, ChatRequestEvent request, CancellationToken ct)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var message = (request.Message ?? string.Empty).Trim();
        if (message.Length == 0)
            throw new ArgumentException("message is required.", nameof(request));
        request.Message = message;

        var ensured = await EnsurePrimaryActorAsync(sessionId, ct);
        var ctx = await EnsureContextAsync(ensured.State, ensured.Role, ensured.Actor.Id);
        var agent = ensured.Agent;
        var actor = ensured.Actor;

        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("N")
            : request.RequestId!.Trim();
        request.RequestId = requestId;
        request.UserId = (request.UserId ?? string.Empty).Trim();
        request.Context[ChatRequest.SessionIdKey] = sessionId;
        request.StreamChunkEveryN = NormalizeStreamChunkEveryN(request.StreamChunkEveryN);
        request.Timestamp ??= Timestamp.FromDateTime(DateTime.UtcNow);

        _logger.LogDebug("[SessionRuntime] SendChatAsync: session={SessionId}, agent={AgentId}, request={RequestId}",
            sessionId, ctx.PrimaryAgentId, requestId);

        // 发布 AG-UI 事件：用户消息 + assistant 开始
        var messageId = $"msg:{sessionId}:assistant:{requestId}";
        ctx.Stream.Publish(new TextMessageStartEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = $"msg:{sessionId}:user:{requestId}",
            Role = "user"
        });
        ctx.Stream.Publish(new TextMessageContentEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = $"msg:{sessionId}:user:{requestId}",
            Delta = message
        });
        ctx.Stream.Publish(new TextMessageEndEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = $"msg:{sessionId}:user:{requestId}"
        });
        ctx.Stream.Publish(new TextMessageStartEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId,
            Role = "assistant"
        });

        // 后台调度 - 通过 actor 发布 ChatRequestEvent，让 event handler 处理并发布 streaming events
        _ = Task.Run(async () =>
        {
            PublishStatus(ctx.Stream, sessionId, requestId, "dispatch.queued", "waiting for run gate", ctx.PrimaryAgentId, ensured.Role.Role);
            await ctx.RunGate.WaitAsync(ct);
            try
            {
                PublishStatus(ctx.Stream, sessionId, requestId, "dispatch.start", "dispatching chat request", ctx.PrimaryAgentId, ensured.Role.Role);
                await _bootstrapper.EnsureInitializedAsync(ctx.Stream, agent, requestId, ensured.Role.Role, CancellationToken.None);

                // 转换 ChatRequestEvent 为 ChatRequest
                var chatRequest = ChatRequest.Create(request.Message ?? string.Empty);
                chatRequest.RequestId = requestId;
                chatRequest.MaxTokens = request.MaxTokens;
                chatRequest.Temperature = request.Temperature;
                if (request.Context.Count > 0)
                {
                    foreach (var (key, value) in request.Context)
                    {
                        chatRequest.Context[key] = value;
                    }
                }

                // 直接调用 ChatStreamAsync（内部通过 event handler 处理 State）
                var chunkEvery = request.StreamChunkEveryN > 0 ? request.StreamChunkEveryN : 1;

                // 注意：不要绑定到请求的 CancellationToken，
                // 否则 HTTP 请求结束会导致 streaming 被提前取消。
                await foreach (var batch in agent.ChatStreamAsync(chatRequest, CancellationToken.None)
                                   .BatchByCountAsync(chunkEvery, CancellationToken.None))
                {
                    ctx.Stream.Publish(new TextMessageContentEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        MessageId = messageId,
                        Delta = batch
                    });
                }

                ctx.Stream.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = messageId
                });
                ctx.Stream.Publish(new RunFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = sessionId,
                    RunId = requestId
                });
                PublishStatus(ctx.Stream, sessionId, requestId, "dispatch.done", "response streamed", ctx.PrimaryAgentId, ensured.Role.Role);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug("[SessionRuntime] Chat canceled: session={SessionId}", sessionId);
                PublishStatus(ctx.Stream, sessionId, requestId, "dispatch.cancel", "request canceled", ctx.PrimaryAgentId, ensured.Role.Role);
                ctx.Stream.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = messageId
                });
                ctx.Stream.Publish(new RunFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = sessionId,
                    RunId = requestId
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SessionRuntime] Chat dispatch failed: session={SessionId}", sessionId);
                PublishStatus(ctx.Stream, sessionId, requestId, "run.error", ex.Message, ctx.PrimaryAgentId, ensured.Role.Role);
                ctx.Stream.Publish(new TextMessageContentEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = messageId,
                    Delta = $"\n[error] {ex.Message}\n"
                });
                ctx.Stream.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = messageId
                });
                ctx.Stream.Publish(new RunFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = sessionId,
                    RunId = requestId
                });
            }
            finally
            {
                ctx.RunGate.Release();
            }
        }, ct);

        return requestId;
    }

    // ============================================================
    //  Workflow 调度（Cognitive Workflow）
    // ============================================================

    public async Task<string> RunWorkflowAsync(
        string sessionId,
        SessionWorkflowRunRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId is required.", nameof(sessionId));

        var state = await _sessions.GetSessionStateAsync(sessionId, ct)
                    ?? throw new InvalidOperationException("session not found.");

        var ctx = await GetOrCreateContextAsync(state, ct);
        var runId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("N")
            : request.RequestId.Trim();

        var runRequest = request with { RequestId = runId };

        _ = Task.Run(async () =>
        {
            await ctx.RunGate.WaitAsync();
            try
            {
                var prepared = await _workflowRunner.PrepareAsync(state, runRequest, _memoryStore != null, CancellationToken.None);
                ctx.Stream.AttachAgent(prepared.CoordinatorActorId);

                ctx.Stream.Publish(new RunStartedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = sessionId,
                    RunId = runId
                });

                await _workflowRunner.ExecuteAsync(prepared, runRequest, CancellationToken.None);

                ctx.Stream.Publish(new RunFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = sessionId,
                    RunId = runId,
                    Result = new { ok = true }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SessionRuntime] Workflow run failed: session={SessionId}", sessionId);
                ctx.Stream.Publish(new RunErrorEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Message = ex.Message,
                    Code = "WORKFLOW_RUN_ERROR"
                });
                ctx.Stream.Publish(new RunFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = sessionId,
                    RunId = runId,
                    Result = new { ok = false, error = ex.Message }
                });
            }
            finally
            {
                ctx.RunGate.Release();
            }
        });

        return runId;
    }

    // ============================================================
    //  内部方法
    // ============================================================

    private async Task<(SessionState State, SessionRole Role, IGAgentActor Actor, AIGAgentBase Agent)>
        EnsurePrimaryActorAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("session not found.");

        var state = await _sessions.GetSessionStateAsync(sessionId, ct);
        if (state == null)
            throw new InvalidOperationException("session not found.");

        var primary = SelectPrimaryRole(state);
        if (primary == null)
            throw new InvalidOperationException("workflow has no roles.");

        var role = await _sessions.EnsureRoleAgentLoadedAsync(state.SessionId, primary.NodeId, ct) ?? primary;
        var actor = await GetOrCreateRoleActorAsync(state.SessionId, role, ct);
        var agent = actor.GetAgent() as AIGAgentBase
            ?? throw new InvalidOperationException("agent not found.");

        _bootstrapper.ConfigureAgentDefaults(actor.Id, agent, _memoryStore != null);
        return (state, role, actor, agent);
    }

    private async Task<IGAgentActor> GetOrCreateRoleActorAsync(string sessionId, SessionRole role, CancellationToken ct)
    {
        var agentId = (role.AgentId ?? string.Empty).Trim();
        if (agentId.Length == 0)
            agentId = BuildRoleActorId(sessionId, role.NodeId ?? string.Empty);

        var actor = await _actorManager.GetActorAsync(agentId);
        if (actor != null)
            return actor;

        var rawId = BuildRoleRawId(sessionId, role.NodeId ?? string.Empty);
        actor = await _actorManager.CreateAndRegisterAsync<RoleAIGAgent>(rawId, ct);
        await _bootstrapper.TryConfigureRoleAgentAsync(actor, role.Role, ct);
        return actor;
    }

    private async Task<SessionContext> EnsureContextAsync(SessionState state, SessionRole primary, string primaryAgentId)
    {
        if (_contexts.TryGetValue(state.SessionId, out var existing))
        {
            if (string.Equals(existing.PrimaryAgentId, primaryAgentId, StringComparison.Ordinal))
            {
                existing.Touch();
                return existing;
            }

            if (_contexts.TryRemove(state.SessionId, out var removed))
                await removed.DisposeAsync();
        }

        if (string.IsNullOrWhiteSpace(primaryAgentId))
            primaryAgentId = BuildRoleActorId(state.SessionId, primary.NodeId ?? string.Empty);

        var stream = new SessionAgUiStream(state.SessionId, primaryAgentId, _streamResolver, _logger);
        var context = new SessionContext(state.SessionId, primaryAgentId, stream);
        _contexts[state.SessionId] = context;
        return context;
    }

    private async Task<SessionContext> GetOrCreateContextAsync(SessionState state, CancellationToken ct)
    {
        var primary = await GetPrimaryRoleAsync(state, ct)
            ?? throw new InvalidOperationException("workflow has no roles.");

        // 确保 actor 存在，否则 stream 订阅会失败
        var actor = await GetOrCreateRoleActorAsync(state.SessionId, primary, ct);
        return await EnsureContextAsync(state, primary, actor.Id);
    }

    private async Task<SessionRole?> GetPrimaryRoleAsync(SessionState state, CancellationToken ct)
    {
        var primary = SelectPrimaryRole(state);
        if (primary == null)
            return null;
        if (!string.IsNullOrWhiteSpace(primary.AgentId) && primary.Loaded)
            return primary;
        return await _sessions.EnsureRoleAgentLoadedAsync(state.SessionId, primary.NodeId, ct) ?? primary;
    }

    private SessionRole? SelectPrimaryRole(SessionState state)
    {
        if (state.Roles.Count == 0)
            return null;

        var preferred = GlobalAgentYamlRegistry.NormalizeRoleKey(_options.CurrentValue.AgentRole);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = state.Roles.FirstOrDefault(r =>
                string.Equals(GlobalAgentYamlRegistry.NormalizeRoleKey(r.Role), preferred, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;

            match = state.Roles.FirstOrDefault(r =>
                string.Equals(GlobalAgentYamlRegistry.NormalizeRoleKey(r.NodeId), preferred, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return state.Roles[0];
    }

    private TimeSpan ResolveIdleTimeout(TimeSpan? idleTimeout)
    {
        if (idleTimeout.HasValue)
            return idleTimeout.Value;

        var minutes = _options.CurrentValue.SessionIdleTimeoutMinutes;
        if (minutes <= 0) minutes = 20;
        return TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 1440));
    }

    private int NormalizeStreamChunkEveryN(int streamChunkEveryN)
    {
        var fallback = _options.CurrentValue.StreamChunkEveryN > 0
            ? _options.CurrentValue.StreamChunkEveryN
            : 1;
        return Math.Clamp(streamChunkEveryN > 0 ? streamChunkEveryN : fallback, 1, 64);
    }

    private static void PublishStatus(
        SessionAgUiStream stream,
        string sessionId,
        string requestId,
        string stage,
        string message,
        string? agentId,
        string? role)
    {
        stream.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "SESSION_STATUS",
            Value = new
            {
                sessionId,
                requestId,
                stage,
                message,
                agentId = agentId ?? string.Empty,
                role = role ?? string.Empty
            }
        });
    }

    private static DateTimeOffset ToDateTimeOffset(Timestamp? ts)
        => ts == null ? DateTimeOffset.UtcNow : new DateTimeOffset(ts.ToDateTime().ToUniversalTime());

    private static string BuildRoleRawId(string sessionId, string nodeId)
    {
        var safeSession = SanitizeRawId(sessionId);
        var safeNode = SanitizeRawId(nodeId);
        if (safeSession.Length == 0) safeSession = "session";
        if (safeNode.Length == 0) safeNode = "node";
        return $"{safeSession}__{safeNode}";
    }

    private static string BuildRoleActorId(string sessionId, string nodeId)
        => AgentId.Normalize<RoleAIGAgent>(BuildRoleRawId(sessionId, nodeId));

    private static string SanitizeRawId(string value)
        => (value ?? string.Empty).Trim().Replace(AgentId.Separator, '_');

    // ============================================================
    //  SessionContext - 极简版
    // ============================================================

    private sealed class SessionContext(string sessionId, string primaryAgentId, SessionAgUiStream stream)
    {
        public string SessionId { get; } = sessionId;
        public string PrimaryAgentId { get; } = primaryAgentId;
        public SessionAgUiStream Stream { get; } = stream;
        public SemaphoreSlim RunGate { get; } = new(1, 1);
        public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

        public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }
}

