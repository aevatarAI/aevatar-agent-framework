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

    public LLMTornadoProvider(
        TornadoApi api,
        ILogger<LLMTornadoProvider> logger,
        LLmProviders providerType,
        string modelName,
        LLMCallPolicy? policy = null)
    {
        _api = api;
        _logger = logger;
        _providerType = providerType;
        _modelName = modelName;
        _policy = policy ?? LLMCallPolicy.Default;
    }

    protected override LLMCallPolicy Policy => _policy;
    protected override ILogger? Logger => _logger;
    protected override string ProviderName => $"LLMTornado:{_providerType}";

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
        // Use model from request settings, or fall back to configured model
        var modelId = request.Settings?.ModelId ?? _modelName;
        
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
                    Arguments = toolCall.FunctionCall.Arguments
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
