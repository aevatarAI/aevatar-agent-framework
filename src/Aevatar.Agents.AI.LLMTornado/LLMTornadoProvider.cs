using System.Runtime.CompilerServices;
using Aevatar.Agents.AI.Abstractions;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.LLMTornado;

public class LLMTornadoProvider : AevatarLLMProviderBase
{
    private readonly TornadoApi _api;
    private readonly ILogger<LLMTornadoProvider> _logger;
    private readonly LLMCallPolicy _policy;
    private readonly LLmProviders _providerType;
    private readonly string _modelName;
    private readonly string _instanceName;

    public LLMTornadoProvider(
        TornadoApi api,
        ILogger<LLMTornadoProvider> logger,
        LLmProviders providerType,
        string modelName,
        string? instanceName = null,
        LLMCallPolicy? policy = null)
    {
        _api = api;
        _logger = logger;
        _providerType = providerType;
        _modelName = modelName;
        _instanceName = string.IsNullOrWhiteSpace(instanceName) ? providerType.ToString() : instanceName.Trim();
        _policy = policy ?? LLMCallPolicy.Default;
    }

    protected override LLMCallPolicy Policy => _policy;
    protected override ILogger? Logger => _logger;
    // IMPORTANT:
    // - CircuitBreaker key uses ProviderName.
    // - Old implementation bucketed by providerType only (e.g., "LLMTornado:Anthropic"), which meant:
    //   one bad Anthropic call could open the circuit for ALL Anthropic usages.
    // - New implementation buckets by provider *instance name* (from LLMProviders:Providers:{name}),
    //   so:
    //   - errors correlate to user secrets config
    //   - circuits are isolated per configured provider instance
    protected override string ProviderName => $"LLMTornado:{_instanceName}";

    protected override async Task<AevatarLLMResponse> GenerateCoreAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var chatRequest = MapToChatRequest(request);
            // ============================================================
            // Critical fix: Force third-party SDK calls to respect CancellationToken
            //
            // Background:
            // - Base layer triggers cancellation via CancelAfter(policy.CallTimeout) to implement timeout
            // - However, LlmTornado's CreateChatCompletion(...) doesn't pass token here, making cancellation/timeout ineffective
            // - Result: Upper-layer Agent (like Trade's Coordinator) will "never get response", appearing as strategy log stuck
            //
            // Solution:
            // - Use Task.WaitAsync(cancellationToken) wrapper
            // - Even if underlying request cannot be truly cancelled, at least upper layer can stop waiting in time, avoiding system deadlock
            // ============================================================
            var responseTask = _api.Chat.CreateChatCompletion(chatRequest);
            var response = await responseTask.WaitAsync(cancellationToken);
            return MapToLLMResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response from LlmTornado ({Provider})", _providerType);
            throw;
        }
    }

    protected override async IAsyncEnumerable<AevatarLLMToken> GenerateStreamCoreAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatRequest = MapToChatRequest(request);

        await foreach (var chunk in _api.Chat.StreamChatEnumerable(chatRequest).WithCancellation(cancellationToken))
        {
            yield return MapToLLMToken(chunk);
        }
    }

    private LlmTornado.Chat.ChatRequest MapToChatRequest(AevatarLLMRequest request)
    {
        // ============================================================
        //  Model resolution (provider-aware)
        //
        // 中文 + ASCII:
        // - Upper layers (AIGAgentBase) may set a global default model (e.g. "gpt-5.1").
        // - But LLMTornado routes by provider: Google(Gemini) cannot accept OpenAI model ids.
        // - Here we resolve/validate the model in ONE place to avoid "cross-provider model" 404s.
        //
        // Design:
        // - If request does not specify model -> use configured provider instance model.
        // - If request specifies an obviously incompatible model for this provider:
        //   - Prefer falling back to configured provider instance model (if compatible)
        //   - Otherwise fail-fast with a clear configuration hint.
        // ============================================================
        var requestedModel = request.Settings?.ModelId;
        var modelId = string.IsNullOrWhiteSpace(requestedModel) ? _modelName : requestedModel.Trim();

        // Guard: Google (Gemini) will 404 when fed OpenAI-style model ids like "gpt-*".
        if (_providerType == LLmProviders.Google && LooksLikeOpenAiChatModel(modelId))
        {
            if (!string.IsNullOrWhiteSpace(_modelName) && !LooksLikeOpenAiChatModel(_modelName))
            {
                _logger.LogWarning(
                    "[LLM] Model override '{RequestedModel}' is incompatible with provider Google(Gemini). Falling back to provider instance model '{ConfiguredModel}' (provider: {ProviderName}).",
                    modelId, _modelName, ProviderName);
                modelId = _modelName;
            }
            else
            {
                throw new ArgumentException(
                    $"LLMTornado provider Google(Gemini) cannot use model '{modelId}'. " +
                    $"Set 'LLMProviders:Providers:{_instanceName}:Model' to a Gemini model (e.g. 'models/gemini-...') or remove the request-level ModelId override.");
            }
        }
        
        // Create ChatModel with model name and provider type for correct routing
        var chatModel = new ChatModel(modelId, _providerType);
        
        var chatRequest = new LlmTornado.Chat.ChatRequest
        {
            Model = chatModel,
            Temperature = request.Settings?.Temperature ?? AevatarAIDefaults.DefaultTemperature,
            MaxTokens = request.Settings?.MaxTokens ?? AevatarAIDefaults.DefaultMaxTokensExtended,
            Messages = []
        };

        if (request.Messages != null)
        {
            foreach (var msg in request.Messages)
            {
                var role = msg.Role switch
                {
                    AevatarChatRole.System => ChatMessageRoles.System,
                    AevatarChatRole.User => ChatMessageRoles.User,
                    AevatarChatRole.Assistant => ChatMessageRoles.Assistant,
                    AevatarChatRole.Tool => ChatMessageRoles.Tool,
                    _ => ChatMessageRoles.User
                };

                var chatMsg = new LlmTornado.Chat.ChatMessage
                {
                    Role = role,
                    Content = msg.Content
                };

                chatRequest.Messages.Add(chatMsg);
            }
        }

        // Add System Prompt if exists and not already in messages
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            if (chatRequest.Messages.All(m => m.Role != ChatMessageRoles.System))
            {
                chatRequest.Messages.Insert(0, new LlmTornado.Chat.ChatMessage
                {
                    Role = ChatMessageRoles.System,
                    Content = request.SystemPrompt
                });
            }
        }

        // Map Tools
        if (request.Functions != null && request.Functions.Count > 0)
        {
            chatRequest.Tools = MapToChatTools(request.Functions);
            chatRequest.ToolChoice = "auto";
        }

        return chatRequest;
    }

    private static bool LooksLikeOpenAiChatModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        // Keep this intentionally narrow:
        // - We only use it to stop the known "gpt-*" -> Gemini mismatch.
        // - Avoid overfitting: other providers may accept arbitrary ids via custom endpoints.
        return modelId.Trim().StartsWith("gpt-", StringComparison.OrdinalIgnoreCase);
    }

    private List<LlmTornado.Common.Tool> MapToChatTools(IList<AevatarFunctionDefinition> functions)
    {
        var tools = new List<LlmTornado.Common.Tool>();

        foreach (var func in functions)
        {
            var tool = new LlmTornado.Common.Tool
            {
                Type = "function",
                Function = new LlmTornado.Common.ToolFunction(
                    func.Name,
                    func.Description,
                    MapFunctionParameters(func.Parameters)
                )
            };
            tools.Add(tool);
        }

        return tools;
    }

    private AevatarLLMResponse MapToLLMResponse(LlmTornado.Chat.ChatResult? response)
    {
        if (response == null || response.Choices == null || response.Choices.Count == 0)
            return new AevatarLLMResponse { Content = string.Empty };

        var choice = response.Choices[0];
        var result = new AevatarLLMResponse
        {
            Content = choice.Message?.Content ?? string.Empty,
            Usage = CreateTokenUsage(
                response.Usage?.PromptTokens,
                response.Usage?.CompletionTokens,
                response.Usage?.TotalTokens)
        };

        // Handle Tool Calls
        if (choice.Message?.ToolCalls != null && choice.Message.ToolCalls.Count > 0)
        {
            var toolCall = choice.Message.ToolCalls[0];

            if (toolCall.FunctionCall != null)
            {
                result.AevatarFunctionCall = new AevatarFunctionCall
                {
                    Name = toolCall.FunctionCall.Name!,
                    Arguments = toolCall.FunctionCall.Arguments ?? ""
                };
            }
        }

        return result;
    }

    private static AevatarLLMToken MapToLLMToken(LlmTornado.Chat.ChatResult? chunk)
    {
        if (chunk == null || chunk.Choices == null || chunk.Choices.Count == 0)
            return new AevatarLLMToken { Content = string.Empty };

        var choice = chunk.Choices[0];
        return new AevatarLLMToken
        {
            Content = choice.Delta?.Content ?? string.Empty
        };
    }
}
