using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Streaming;

namespace Aevatar.Agents.Workspaces.Hubs;

public sealed class RoleAgUiHub : IAgUiEventStream
{
    // ============================================================
    // 中文 + ASCII:
    // 角色编排专用 AG-UI Hub，统一收敛 stream chunk + snapshot。
    // ============================================================
    private readonly BroadcastEventHub<AgUiEvent> _events = new(
        replayBufferSize: 0,
        subscriberBufferSize: 4096,
        warningLogger: null,
        hubName: "role-workspace-agui");

    private readonly object _gate = new();
    private readonly List<AgUiMessage> _messages = new();
    private readonly HashSet<string> _streamingMessages = new(StringComparer.Ordinal);

    public IReadOnlyList<AgUiMessage> GetMessagesSnapshot(int maxMessages)
    {
        lock (_gate)
        {
            if (_messages.Count <= maxMessages)
                return _messages.ToList();
            return _messages.TakeLast(maxMessages).ToList();
        }
    }

    public IReadOnlyList<AgUiMessage> GetMessagesSnapshot(string role, int maxMessages)
    {
        var prefix = BuildMessagePrefix(role);
        lock (_gate)
        {
            var list = _messages
                .Where(m => m.Id.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            if (list.Count <= maxMessages)
                return list;
            return list.TakeLast(maxMessages).ToList();
        }
    }

    public IAsyncEnumerable<AgUiEvent> SubscribeAsync(CancellationToken ct)
        => _events.SubscribeAsync(replay: false, ct);

    public void PublishUserMessage(string role, string requestId, string content)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return;

        var messageId = BuildMessageId(role, requestId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _events.Publish(new TextMessageStartEvent
        {
            Timestamp = now,
            MessageId = messageId,
            Role = "user"
        });
        _events.Publish(new TextMessageContentEvent
        {
            Timestamp = now,
            MessageId = messageId,
            Delta = content ?? string.Empty
        });
        _events.Publish(new TextMessageEndEvent
        {
            Timestamp = now,
            MessageId = messageId
        });

        UpsertMessage(messageId, "user", content ?? string.Empty);
    }

    public void ClearRoleMessages(string role)
    {
        var prefix = BuildMessagePrefix(role);
        lock (_gate)
        {
            _messages.RemoveAll(m => m.Id.StartsWith(prefix, StringComparison.Ordinal));
            var pending = _streamingMessages
                .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (var id in pending)
            {
                _streamingMessages.Remove(id);
            }
        }
    }

    public void PublishAssistantChunk(string role, string requestId, string content)
    {
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrEmpty(content))
            return;

        var messageId = BuildMessageId(role, requestId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var roleValue = NormalizeRole(role);

        var isFirstChunk = false;
        lock (_gate)
        {
            if (_streamingMessages.Add(messageId))
                isFirstChunk = true;
        }

        if (isFirstChunk)
        {
            _events.Publish(new TextMessageStartEvent
            {
                Timestamp = now,
                MessageId = messageId,
                Role = roleValue
            });
        }

        _events.Publish(new TextMessageContentEvent
        {
            Timestamp = now,
            MessageId = messageId,
            Delta = content
        });
    }

    public void PublishAssistantEnd(string role, string requestId, string content)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return;

        var messageId = BuildMessageId(role, requestId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var roleValue = NormalizeRole(role);

        bool hadChunks;
        lock (_gate)
        {
            hadChunks = _streamingMessages.Remove(messageId);
        }

        if (!hadChunks && !string.IsNullOrWhiteSpace(content))
        {
            _events.Publish(new TextMessageStartEvent
            {
                Timestamp = now,
                MessageId = messageId,
                Role = roleValue
            });
            _events.Publish(new TextMessageContentEvent
            {
                Timestamp = now,
                MessageId = messageId,
                Delta = content
            });
        }

        _events.Publish(new TextMessageEndEvent
        {
            Timestamp = now,
            MessageId = messageId
        });

        UpsertMessage(messageId, roleValue, content ?? string.Empty);
    }

    public void PublishDelegation(string fromRole, string toRole, string message, string? requestId = null)
    {
        var from = NormalizeRole(fromRole);
        var to = NormalizeRole(toRole);
        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = string.IsNullOrWhiteSpace(requestId)
            ? Guid.NewGuid().ToString("N")
            : requestId.Trim();
        var messageId = BuildDelegationMessageId(id);
        var content = BuildDelegationContent(from, to, message);

        _events.Publish(new CustomEvent
        {
            Timestamp = now,
            Name = "WORKSPACE_DELEGATION",
            Value = new
            {
                fromRole = from,
                toRole = to,
                message = message ?? string.Empty,
                requestId = id
            }
        });

        _events.Publish(new TextMessageStartEvent
        {
            Timestamp = now,
            MessageId = messageId,
            Role = "system"
        });
        _events.Publish(new TextMessageContentEvent
        {
            Timestamp = now,
            MessageId = messageId,
            Delta = content
        });
        _events.Publish(new TextMessageEndEvent
        {
            Timestamp = now,
            MessageId = messageId
        });

        UpsertMessage(messageId, "system", content);
    }

    public void PublishCustomEvent(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _events.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = name.Trim(),
            Value = value
        });
    }

    private void UpsertMessage(string messageId, string role, string content)
    {
        lock (_gate)
        {
            _messages.RemoveAll(m => string.Equals(m.Id, messageId, StringComparison.Ordinal));
            _messages.Add(new AgUiMessage
            {
                Id = messageId,
                Role = role,
                Content = content
            });
        }
    }

    private static string BuildMessageId(string role, string requestId)
        => $"msg:role:{NormalizeRole(role)}:{requestId}";

    private static string BuildMessagePrefix(string role)
        => $"msg:role:{NormalizeRole(role)}:";

    private static string BuildDelegationMessageId(string requestId)
        => $"msg:delegation:{requestId}";

    private static string BuildDelegationContent(string fromRole, string toRole, string message)
    {
        var head = string.IsNullOrWhiteSpace(fromRole) && string.IsNullOrWhiteSpace(toRole)
            ? "delegation"
            : $"{fromRole} -> {toRole}".Trim();
        var body = (message ?? string.Empty).Trim();
        if (body.Length == 0)
            return $"[delegation] {head}";
        return $"[delegation] {head}\n{body}";
    }

    private static string NormalizeRole(string? role)
    {
        var trimmed = (role ?? string.Empty).Trim();
        return trimmed.Length == 0 ? "assistant" : trimmed.ToLowerInvariant();
    }
}
