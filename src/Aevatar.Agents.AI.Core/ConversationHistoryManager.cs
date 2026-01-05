using Aevatar.Agents.AI.Abstractions;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Provides basic conversation history management for AI agents.
/// </summary>
public class ConversationHistoryManager
{
    private readonly RepeatedField<AevatarChatMessage> _history;

    public ConversationHistoryManager(RepeatedField<AevatarChatMessage> history)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    protected RepeatedField<AevatarChatMessage> History => _history;

    /// <summary>
    /// Adds a message to the conversation history.
    /// </summary>
    public virtual void AddMessage(string content, AevatarChatRole role, string? name = null)
    {
        var message = new AevatarChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            message.Metadata[name] = string.Empty;
        }

        _history.Add(message);
    }

    /// <summary>
    /// Adds a pre-constructed message to the conversation history.
    /// </summary>
    public virtual void AddMessage(AevatarChatMessage message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (message.Timestamp == null)
        {
            message.Timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        }

        _history.Add(message);
    }

    public virtual void Clear() => _history.Clear();

    public int MessageCount => _history.Count;
}
