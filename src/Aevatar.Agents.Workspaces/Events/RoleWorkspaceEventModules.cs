using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.Workspaces.Hubs;
using Aevatar.Agents.Workspaces.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Workspaces.Events;

public sealed class RoleWorkspaceEventModuleFactory : IEventModuleFactory
{
    private readonly RoleAgUiHub _hub;
    private readonly ILogger<RoleWorkspaceEventModuleFactory>? _logger;

    public RoleWorkspaceEventModuleFactory(
        RoleAgUiHub hub,
        ILogger<RoleWorkspaceEventModuleFactory>? logger = null)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger;
    }

    public bool TryCreate(string? name, out IEventModule module)
    {
        var key = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (key == RoleWorkspaceChatTraceModule.ModuleName)
        {
            module = new RoleWorkspaceChatTraceModule();
            return true;
        }

        if (key == RoleWorkspacePingModule.ModuleName)
        {
            module = new RoleWorkspacePingModule(_hub);
            return true;
        }

        if (key == RoleWorkspaceGroupAgUiModule.ModuleName)
        {
            module = new RoleWorkspaceGroupAgUiModule(_hub, _logger);
            return true;
        }

        if (key == RoleWorkspaceGroupTaskModule.ModuleName)
        {
            module = new RoleWorkspaceGroupTaskModule(_hub, _logger);
            return true;
        }

        module = null!;
        return false;
    }
}

public sealed class RoleWorkspaceChatTraceModule : IEventModule
{
    public const string ModuleName = "workspace_chat_trace";

    public string Name => ModuleName;
    public int Priority => 0;

    public bool CanHandle(EventEnvelope envelope)
    {
        return envelope.Payload?.Is(ChatRequestEvent.Descriptor) == true ||
               envelope.Payload?.Is(ChatResponseEvent.Descriptor) == true;
    }

    public Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
    {
        if (envelope.Payload == null)
            return Task.CompletedTask;

        ExecutionTraceEvent trace;
        if (envelope.Payload.Is(ChatRequestEvent.Descriptor))
        {
            var evt = envelope.Payload.Unpack<ChatRequestEvent>();
            trace = BuildTrace(
                host.AgentId,
                ExecutionTraceEventPhase.LlmRequest,
                $"[agent.yaml] chat request: {Trim(evt.Message)}");
        }
        else if (envelope.Payload.Is(ChatResponseEvent.Descriptor))
        {
            var evt = envelope.Payload.Unpack<ChatResponseEvent>();
            var length = evt.Content?.Length ?? 0;
            trace = BuildTrace(
                host.AgentId,
                ExecutionTraceEventPhase.LlmResponse,
                $"[agent.yaml] chat response (len={length})");
        }
        else
        {
            return Task.CompletedTask;
        }

        return host.PublishAsync(trace, EventDirection.Down, ct);
    }

    private static ExecutionTraceEvent BuildTrace(string agentId, string phase, string message)
    {
        var trace = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Phase = phase,
            Message = message,
            NodeId = agentId
        };

        trace.Fields[ExecutionTraceEventFields.AgentId] =
            ExecutionTraceEventFieldValue.FromString(agentId);

        return trace;
    }

    private static string Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty)";

        var trimmed = value.Trim();
        return trimmed.Length <= 120
            ? trimmed
            : $"{trimmed[..120]}...";
    }
}

public sealed class RoleWorkspacePingModule : IEventModule
{
    public const string ModuleName = "workspace_ping";

    private readonly RoleAgUiHub _hub;

    public RoleWorkspacePingModule(RoleAgUiHub hub)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    }

    public string Name => ModuleName;
    public int Priority => 0;

    public bool CanHandle(EventEnvelope envelope)
        => envelope.Payload?.Is(WorkspacePingEvent.Descriptor) == true;

    public Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
    {
        if (envelope.Payload == null)
            return Task.CompletedTask;

        var evt = envelope.Payload.Unpack<WorkspacePingEvent>();
        var response = new WorkspacePongEvent
        {
            RequestId = evt.RequestId,
            Content = $"pong: {evt.Content}".Trim(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _hub.PublishCustomEvent("WORKSPACE_PONG", new
        {
            requestId = response.RequestId,
            content = response.Content
        });

        return host.PublishAsync(response, EventDirection.Down, ct);
    }
}

public sealed class RoleWorkspaceGroupAgUiModule : IEventModule, IRouteBypassModule
{
    // ============================================================
    // 中文 + ASCII:
    // 将 ChatStream/Response 投影到 Role Workspace 的 AG-UI Hub。
    // ============================================================
    public const string ModuleName = "workspace_group_agui";

    private readonly RoleAgUiHub _hub;
    private readonly ILogger? _logger;

    public RoleWorkspaceGroupAgUiModule(RoleAgUiHub hub, ILogger? logger = null)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger;
    }

    public string Name => ModuleName;
    public int Priority => 0;

    public bool CanHandle(EventEnvelope envelope)
    {
        return envelope.Payload?.Is(ChatStreamChunkEvent.Descriptor) == true ||
               envelope.Payload?.Is(ChatResponseEvent.Descriptor) == true;
    }

    public Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
    {
        if (envelope.Payload == null)
            return Task.CompletedTask;

        var role = ResolveRole(host);
        if (envelope.Payload.Is(ChatStreamChunkEvent.Descriptor))
        {
            var evt = envelope.Payload.Unpack<ChatStreamChunkEvent>();
            if (!string.IsNullOrWhiteSpace(evt.RequestId))
            {
                _hub.PublishAssistantChunk(role, evt.RequestId, evt.Content ?? string.Empty);
            }
        }
        else if (envelope.Payload.Is(ChatResponseEvent.Descriptor))
        {
            var evt = envelope.Payload.Unpack<ChatResponseEvent>();
            if (!string.IsNullOrWhiteSpace(evt.RequestId))
            {
                _hub.PublishAssistantEnd(role, evt.RequestId, evt.Content ?? string.Empty);
            }
        }

        return Task.CompletedTask;
    }

    private static string ResolveRole(IEventModuleHost host)
    {
        if (host.Agent is RoleAIGAgent roleAgent)
        {
            var role = GlobalAgentYamlRegistry.NormalizeRoleKey(roleAgent.Role);
            if (!string.IsNullOrWhiteSpace(role))
                return role;
        }

        return GlobalAgentYamlRegistry.NormalizeRoleKey(host.AgentId);
    }
}

public sealed class RoleWorkspaceGroupTaskModule : IEventModule, IRouteBypassModule
{
    // ============================================================
    // 中文 + ASCII:
    // 处理 publish_event 下发的任务，路由到目标 role 的 ChatRequest。
    // ============================================================
    public const string ModuleName = "workspace_group_task";
    private const string ChatEventType = "workspace.role.chat";

    private readonly RoleAgUiHub _hub;
    private readonly ILogger? _logger;

    public RoleWorkspaceGroupTaskModule(RoleAgUiHub hub, ILogger? logger = null)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger;
    }

    public string Name => ModuleName;
    public int Priority => 0;

    public bool CanHandle(EventEnvelope envelope)
        => envelope.Payload?.Is(AevatarToolPublishedEvent.Descriptor) == true;

    public Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
    {
        if (envelope.Payload == null)
            return Task.CompletedTask;

        var evt = envelope.Payload.Unpack<AevatarToolPublishedEvent>();
        if (!string.Equals(evt.EventType, ChatEventType, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        if (string.Equals(evt.AgentId, host.AgentId, StringComparison.Ordinal))
            return Task.CompletedTask;

        if (!TryParsePayload(evt.PayloadJson, out var payload))
            return Task.CompletedTask;

        var targetRole = payload.TargetRole;
        if (string.IsNullOrWhiteSpace(targetRole))
            return Task.CompletedTask;

        var currentRole = ResolveRole(host);
        if (!string.Equals(currentRole, targetRole, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(payload.Message))
            return Task.CompletedTask;

        var publisherRole = ResolvePublisherRole(evt.AgentId);
        _hub.PublishDelegation(
            publisherRole,
            targetRole,
            payload.Message,
            payload.RequestId);

        var request = new ChatRequestEvent
        {
            RequestId = string.IsNullOrWhiteSpace(payload.RequestId)
                ? Guid.NewGuid().ToString("N")
                : payload.RequestId,
            Message = payload.Message.Trim(),
            UserId = payload.UserId ?? string.Empty,
            Temperature = payload.Temperature,
            MaxTokens = payload.MaxTokens,
            StreamChunkEveryN = payload.StreamChunkEveryN,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        if (payload.Context.Count > 0)
        {
            foreach (var (key, value) in payload.Context)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    request.Context[key] = value ?? string.Empty;
                }
            }
        }

        request.Context["delegated_from"] = evt.AgentId ?? string.Empty;
        request.Context["target_role"] = targetRole;
        if (!request.Context.ContainsKey(ChatRequest.SessionIdKey))
        {
            request.Context[ChatRequest.SessionIdKey] = "role_workspace";
        }

        return host.PublishAsync(request, EventDirection.Down, ct);
    }

    private bool TryParsePayload(string? json, out GroupTaskPayload payload)
    {
        payload = GroupTaskPayload.Empty;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var root = doc.RootElement;
            payload = new GroupTaskPayload(
                TargetRole: NormalizeRole(ReadString(root, "target_role")),
                Message: ReadString(root, "message"),
                RequestId: ReadString(root, "request_id"),
                UserId: ReadString(root, "user_id"),
                Temperature: ReadDouble(root, "temperature"),
                MaxTokens: ReadInt(root, "max_tokens"),
                StreamChunkEveryN: ReadInt(root, "stream_chunk_every_n"),
                Context: ReadContext(root));

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RoleWorkspaceGroupTask] Failed to parse payload json");
            return false;
        }
    }

    private static string ResolveRole(IEventModuleHost host)
    {
        if (host.Agent is RoleAIGAgent roleAgent)
        {
            var role = GlobalAgentYamlRegistry.NormalizeRoleKey(roleAgent.Role);
            if (!string.IsNullOrWhiteSpace(role))
                return role;
        }

        return GlobalAgentYamlRegistry.NormalizeRoleKey(host.AgentId);
    }

    private static string ResolvePublisherRole(string? agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return string.Empty;

        var raw = agentId;
        var idx = agentId.LastIndexOf(':');
        if (idx >= 0 && idx < agentId.Length - 1)
            raw = agentId[(idx + 1)..];

        return NormalizeRole(raw);
    }

    private static string NormalizeRole(string? role)
        => GlobalAgentYamlRegistry.NormalizeRoleKey(role);

    private static string ReadString(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static int ReadInt(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var parsed))
                return parsed;
        }

        return 0;
    }

    private static float ReadDouble(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetDouble(out var parsed))
                return (float)parsed;
        }

        return 0;
    }

    private static Dictionary<string, string> ReadContext(JsonElement root)
    {
        if (!root.TryGetProperty("context", out var value) || value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>();

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in value.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }

        return dict;
    }

    private sealed record GroupTaskPayload(
        string TargetRole,
        string Message,
        string RequestId,
        string UserId,
        float Temperature,
        int MaxTokens,
        int StreamChunkEveryN,
        Dictionary<string, string> Context)
    {
        public static readonly GroupTaskPayload Empty = new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            0,
            new Dictionary<string, string>());
    }
}
