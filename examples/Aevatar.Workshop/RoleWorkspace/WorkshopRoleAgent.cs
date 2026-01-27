using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Workshop.RoleWorkspace;

public sealed class WorkshopRoleAgent : RoleAIGAgent
{
    // ============================================================
    // 中文 + ASCII:
    // Role agent for workshop workspace, 支持 self-handling + publish_event。
    // ============================================================
    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"WorkshopRoleAgent({Role})");

    protected override IReadOnlySet<string> GetYamlDangerousToolNames()
    {
        var names = new HashSet<string>(base.GetYamlDangerousToolNames(), StringComparer.OrdinalIgnoreCase)
        {
            "publish_event"
        };
        return names;
    }

    [EventHandler(AllowSelfHandling = true)]
    protected override async Task HandleChatRequestEvent(ChatRequestEvent evt)
    {
        if (evt == null)
            return;

        var requestId = string.IsNullOrWhiteSpace(evt.RequestId)
            ? Guid.NewGuid().ToString("N")
            : evt.RequestId;

        var request = new ChatRequest
        {
            RequestId = requestId,
            Message = evt.Message ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(evt.UserId))
        {
            request.Context["user_id"] = evt.UserId.Trim();
        }

        if (evt.Context != null)
        {
            foreach (var (key, value) in evt.Context)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    request.Context[key] = value ?? string.Empty;
                }
            }
        }

        if (evt.MaxTokens > 0)
        {
            request.MaxTokens = evt.MaxTokens;
        }

        if (evt.Temperature > 0)
        {
            request.Temperature = evt.Temperature;
        }

        const int DefaultStreamChunkEveryN = 8;
        var chunkEvery = evt.StreamChunkEveryN > 0 ? evt.StreamChunkEveryN : DefaultStreamChunkEveryN;
        chunkEvery = Math.Clamp(chunkEvery, 1, 128);

        var buffer = new StringBuilder();
        var pending = new StringBuilder();
        var rawChunkCount = 0;
        var publishedIndex = 0;

        await foreach (var chunk in ChatStreamAsync(request, CancellationToken.None))
        {
            if (string.IsNullOrEmpty(chunk))
                continue;

            buffer.Append(chunk);
            pending.Append(chunk);
            rawChunkCount++;

            if (rawChunkCount % chunkEvery != 0)
                continue;

            publishedIndex++;
            await PublishAsync(new ChatStreamChunkEvent
            {
                RequestId = requestId,
                Content = pending.ToString(),
                ChunkIndex = publishedIndex,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            pending.Clear();
        }

        if (pending.Length > 0)
        {
            publishedIndex++;
            await PublishAsync(new ChatStreamChunkEvent
            {
                RequestId = requestId,
                Content = pending.ToString(),
                ChunkIndex = publishedIndex,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        await PublishAsync(new ChatResponseEvent
        {
            RequestId = requestId,
            Content = buffer.ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }
}
