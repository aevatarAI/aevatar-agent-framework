using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
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
//  删除的冗余：
//  - 复杂的 status 事件
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
        IEnumerable<IMemoryStore> memoryStores,
        ILogger<SessionRuntime> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
        _streamResolver = streamResolver ?? throw new ArgumentNullException(nameof(streamResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _workflowCatalog = workflowCatalog ?? throw new ArgumentNullException(nameof(workflowCatalog));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _memoryStore = memoryStores?.FirstOrDefault();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ============================================================
    //  Session CRUD
    // ============================================================

    public async Task<SessionState> CreateSessionAsync(string? workflowName, CancellationToken ct)
    {
        var name = _workflowCatalog.ResolveWorkflowName(workflowName);
        var state = await _sessions.StartSessionAsync(new StartSessionRequest { WorkflowName = name }, ct);
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

        // 后台调度 - 使用 StreamingContext 让 HandleChatRequestEvent 直接发布到 UI
        _ = Task.Run(async () =>
        {
            await ctx.RunGate.WaitAsync();
            try
            {
                await _bootstrapper.EnsureInitializedAsync(ctx.Stream, agent, requestId, ensured.Role.Role, CancellationToken.None);

                // 注册 StreamingContext，让 RoleAIGAgent 直接发布到 UI
                // 注意：使用 requestId 作为 key，因为 AsyncLocal 跨不了 stream 回调边界
                var sink = new SessionStreamChunkSink(ctx.Stream, messageId);
                StreamingContext.Register(requestId, sink);
                try
                {
                    await actor.PublishEventAsync(request, EventDirection.Down, CancellationToken.None);
                    
                    // 等待 event handler 完成 - sink 会在 EmitEnd 时标记完成
                    await sink.WaitForCompletionAsync(TimeSpan.FromMinutes(10));
                }
                finally
                {
                    StreamingContext.Unregister(requestId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SessionRuntime] Chat dispatch failed: session={SessionId}", sessionId);
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
            }
            finally
            {
                ctx.RunGate.Release();
            }
        });

        return requestId;
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

// ============================================================
//  SessionStreamChunkSink - 实现 IStreamChunkSink
//
//  让 RoleAIGAgent.HandleChatRequestEvent 能直接把 chunks 发布到 UI
// ============================================================

internal sealed class SessionStreamChunkSink : IStreamChunkSink
{
    private readonly SessionAgUiStream _stream;
    private readonly string _messageId;
    private readonly TaskCompletionSource _completionSource = new();

    public SessionStreamChunkSink(SessionAgUiStream stream, string messageId)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _messageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
    }

    public void EmitChunk(string requestId, string content)
    {
        _stream.Publish(new TextMessageContentEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = _messageId,
            Delta = content
        });
    }

    public void EmitEnd(string requestId, string? fullContent)
    {
        _stream.Publish(new TextMessageEndEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = _messageId
        });
        _stream.Publish(new RunFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ThreadId = _messageId.Split(':').ElementAtOrDefault(1) ?? string.Empty,
            RunId = requestId
        });
        
        // 标记完成
        _completionSource.TrySetResult();
    }

    public async Task WaitForCompletionAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _completionSource.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout - 不等了，sink 可能没被使用（非 streaming 路径）
        }
    }
}
