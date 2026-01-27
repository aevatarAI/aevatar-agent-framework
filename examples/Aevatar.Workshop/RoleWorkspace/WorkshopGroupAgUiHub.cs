using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.AI.Core.Configuration;

namespace Aevatar.Workshop.RoleWorkspace;

public sealed class WorkshopGroupAgUiHub
{
    // ============================================================
    // 中文 + ASCII:
    // 角色编排专用 AG-UI Hub，统一收敛 stream chunk + snapshot。
    // ============================================================
    private readonly BroadcastEventHub<AgUiEvent> _events = new(
        replayBufferSize: 0,
        subscriberBufferSize: 4096,
        warningLogger: null,
        hubName: "workshop-role-agui");

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

    public IAsyncEnumerable<AgUiEvent> SubscribeAsync(CancellationToken ct)
        => _events.SubscribeAsync(replay: false, ct);

    public void PublishUserMessage(string requestId, string content)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return;

        var messageId = BuildMessageId("user", requestId);
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
            Name = "WORKSHOP_DELEGATION",
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
    {
        var roleKey = NormalizeRole(role);
        return $"msg:role:{roleKey}:{requestId}";
    }

    private static string BuildDelegationMessageId(string requestId)
        => $"msg:delegation:{requestId}";

    private static string BuildDelegationContent(string fromRole, string toRole, string? message)
    {
        var summary = $"delegate: {fromRole} -> {toRole}";
        var body = (message ?? string.Empty).Trim();
        return body.Length == 0
            ? summary
            : $"{summary}\n{body}";
    }

    private static string NormalizeRole(string? role)
    {
        var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
        return string.IsNullOrWhiteSpace(key) ? "assistant" : key;
    }
}
