using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;

namespace ProgressHookChatWebDemo;

public sealed class ProgressChatAgent : AIGAgentBase
{
    public ProgressChatAgent()
    {
    }

    public ProgressChatAgent(string id) : base(id)
    {
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult("ProgressHookChatWebDemo:ProgressChatAgent");

    [EventHandler]
    public async Task HandleChatRequestAsync(ChatRequestEvent evt)
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

        var buffer = new StringBuilder();
        try
        {
            await foreach (var chunk in ChatStreamAsync(request, CancellationToken.None))
            {
                if (string.IsNullOrEmpty(chunk))
                    continue;

                buffer.Append(chunk);
                await PublishAsync(new StringValue { Value = $"{requestId}|assistant|{chunk}" });
            }

            await PublishAsync(new ChatResponseEvent
            {
                RequestId = requestId,
                Content = buffer.ToString(),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        catch (Exception ex)
        {
            await PublishAsync(new StringValue
            {
                Value = $"{requestId}|assistant|\n[demo] LLM error: {ex.Message}\n"
            });

            await PublishAsync(new ChatResponseEvent
            {
                RequestId = requestId,
                Content = buffer.ToString(),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
    }
}
