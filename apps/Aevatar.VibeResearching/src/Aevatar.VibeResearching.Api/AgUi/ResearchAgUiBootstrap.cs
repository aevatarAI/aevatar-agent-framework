using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;

namespace VibeResearching.Api.AgUi;

// ============================================================
//  Research AG-UI Bootstrap (Snapshot-first reconnect)
//
//  WHY:
//  - SSE reconnect should not replay token/tool spam.
//  - Instead, send deterministic snapshots:
//    - MESSAGES_SNAPSHOT: last N user/assistant messages from AIGAgentBase.State.History
// ============================================================

internal static class ResearchAgUiBootstrap
{
    public static async Task<List<AgUiMessage>> BuildMessagesSnapshotAsync(
        string sessionId,
        ResearchRuntime runtime,
        int maxMessages,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
            return [];

        maxMessages = Math.Clamp(maxMessages, 0, 200);
        if (maxMessages == 0)
            return [];

        // Ensure agent exists so we can read State.History (even if empty).
        await runtime.GetAgentAsync(sessionId, providerName: null, ct);

        var state = await runtime.TryGetAgentStateAsync(sessionId, ct);
        var history = state?.History;
        if (history is null || history.Count == 0)
            return [];

        var take = Math.Min(maxMessages, history.Count);
        var slice = history.Skip(Math.Max(0, history.Count - take)).ToList();

        var messages = new List<AgUiMessage>(capacity: slice.Count);
        for (var i = 0; i < slice.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var m = slice[i];
            if (m == null) continue;

            // Keep snapshot concise: only user/assistant/system messages.
            // Tool transcripts can be huge; tool visibility should come from CUSTOM events.
            var role = m.Role switch
            {
                AevatarChatRole.User => "user",
                AevatarChatRole.Assistant => "assistant",
                AevatarChatRole.System => "system",
                _ => null
            };

            if (role == null)
                continue;

            var content = (m.Content ?? string.Empty).TrimEnd();
            if (content.Length == 0)
                continue;

            var ts = m.Timestamp?.ToDateTimeOffset().ToUnixTimeMilliseconds() ?? 0;
            var messageId = $"msg:{sessionId}:{ts}:{role}:{i}";

            messages.Add(new AgUiMessage
            {
                Id = messageId,
                Role = role,
                Content = content
            });
        }

        return messages;
    }
}


