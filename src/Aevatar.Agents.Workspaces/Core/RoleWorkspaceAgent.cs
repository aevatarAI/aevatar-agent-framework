using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Workspaces.Core;

public sealed class RoleWorkspaceAgent : RoleAIGAgent
{
    // ============================================================
    // 中文 + ASCII:
    // Role workspace agent，支持 self-handling + publish_event。
    // ============================================================
    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"RoleWorkspaceAgent({Role})");

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

        bool supportsStreaming;
        try
        {
            supportsStreaming = await SupportsStreamingAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort: default to streaming path if provider probe fails.
            supportsStreaming = true;
        }

        if (!supportsStreaming)
        {
            var response = await ChatAsync(request, CancellationToken.None);
            await PublishAsync(new ChatResponseEvent
            {
                RequestId = requestId,
                Content = response.Content ?? string.Empty,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            return;
        }

        const int DefaultStreamChunkEveryN = 8;
        var chunkEvery = evt.StreamChunkEveryN > 0 ? evt.StreamChunkEveryN : DefaultStreamChunkEveryN;

        var buffer = new StringBuilder();
        var publishedIndex = 0;

        await foreach (var batch in ChatStreamAsync(request, CancellationToken.None)
                           .BatchByCountAsync(chunkEvery, CancellationToken.None))
        {
            if (string.IsNullOrEmpty(batch))
                continue;

            buffer.Append(batch);
            publishedIndex++;
            await PublishAsync(new ChatStreamChunkEvent
            {
                RequestId = requestId,
                Content = batch,
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
