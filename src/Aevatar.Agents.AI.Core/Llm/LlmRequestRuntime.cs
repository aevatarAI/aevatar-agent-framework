using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;

namespace Aevatar.Agents.AI.Core;

internal sealed class LlmRequestRuntime
{
    private readonly ILlmRequestHost _host;

    internal LlmRequestRuntime(ILlmRequestHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    internal AevatarLLMRequest BuildRequest(ChatRequest request)
    {
        var settings = _host.GetLLMSettings(request);
        if (request.StopSequences.Count > 0)
        {
            settings.StopSequences = request.StopSequences.ToList();
        }

        // Build message list (priority order):
        // 1. External history from request.History (caller-provided, e.g. from session state)
        // 2. Internal State.History (when EnableChatHistoryInState is true)
        // 3. Current user message (always appended last)
        var messages = new List<AevatarChatMessage>();
        if (request.History.Count > 0)
        {
            // External history provided by caller — use it directly
            foreach (var msg in request.History)
            {
                messages.Add(msg);
            }
        }
        else if (_host.EnableChatHistoryInState)
        {
            // Fallback: use internal state history
            var history = _host.SnapshotChatHistoryMessages();
            if (history.Count > 0)
                messages.AddRange(history);
        }

        messages.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.User,
            Content = request.Message
        });

        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = _host.BuildEffectiveSystemPromptWithSummary(),
            Messages = messages,
            Settings = settings
        };

        // Apply baseline allowlist before attaching functions so schema is filtered accordingly.
        var fixedAllowlist = _host.FixedToolAllowlist;
        if (fixedAllowlist is { Count: > 0 })
        {
            llmRequest.Context ??= new Dictionary<string, object>();
            llmRequest.Context[AIGAgentKeys.ToolAllowlist] = fixedAllowlist;
        }

        _host.AttachToolsToRequest(llmRequest);

        if (!string.IsNullOrWhiteSpace(request.StageHint))
        {
            llmRequest.Context ??= new Dictionary<string, object>();
            llmRequest.Context["stage_hint"] = request.StageHint!;
        }

        return llmRequest;
    }
}

