using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithProcessStrategy.Strategies;

/// <summary>
/// Standard AI processing strategy - simple pass-through to LLM
/// </summary>
public class StandardProcessingStrategy : IAevatarAIProcessingStrategy
{
    /// <inheritdoc />
    public string Name => "Standard Processing";

    /// <inheritdoc />
    public string Description => "Standard AI processing strategy - Directly passes requests to LLM provider, supports conversation history and tool calling";

    /// <inheritdoc />
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.Standard;

    /// <inheritdoc />
    public bool CanHandle(AevatarAIContext context)
    {
        // Standard strategy can handle all basic requests
        // But if context explicitly specifies another strategy, return false
        if (context.Metadata?.ContainsKey("PreferredStrategy") == true)
        {
            var preferred = context.Metadata["PreferredStrategy"]?.ToString();
            return string.Equals(preferred, "Standard", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(preferred, Name, StringComparison.OrdinalIgnoreCase);
        }

        // Standard strategy suitable for simple Q&A
        return true;
    }

    /// <inheritdoc />
    public double EstimateComplexity(AevatarAIContext context)
    {
        // Estimate complexity based on question length and conversation history
        var questionLength = context.Question?.Length ?? 0;
        var historyCount = context.ConversationHistory?.Count ?? 0;

        // Simple heuristic calculation
        var complexity = 0.0;

        // Question length impact (0-0.3)
        complexity += Math.Min(questionLength / 1000.0, 0.3);

        // Conversation history impact (0-0.3)
        complexity += Math.Min(historyCount / 20.0, 0.3);

        // If tool calling needed, increase complexity
        if (context.Metadata?.ContainsKey("ExpectsToolUse") == true)
        {
            complexity += 0.2;
        }

        // Standard strategy suitable for low to medium complexity
        return Math.Min(complexity, 0.5);
    }

    /// <inheritdoc />
    public bool ValidateRequirements(AevatarAIStrategyDependencies dependencies)
    {
        // Validate required dependencies
        if (dependencies == null)
        {
            return false;
        }

        // LLM provider is required
        if (dependencies.LLMProvider == null)
        {
            dependencies.Logger?.LogError("StandardProcessingStrategy requires LLMProvider");
            return false;
        }

        // Configuration is required
        if (dependencies.Configuration == null)
        {
            dependencies.Logger?.LogError("StandardProcessingStrategy requires Configuration");
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        dependencies.Logger?.LogDebug("Processing with Standard strategy for question: {Question}",
            context.Question);

        try
        {
            // Build the LLM request with system prompt and user question
            var request = new AevatarLLMRequest
            {
                SystemPrompt = context.SystemPrompt ?? "You are a helpful AI assistant.",
                UserPrompt = context.Question
            };

            // Add conversation history if available  
            if (context.ConversationHistory?.Count > 0)
            {
                // ConversationHistory is List<AevatarConversationEntry>, convert to messages
                foreach (var entry in context.ConversationHistory)
                {
                    var message = new AevatarChatMessage
                    {
                        Role = ParseChatRole(entry.Role),
                        Content = entry.Content,
                        Timestamp = DateTime.UtcNow.ToTimestamp()
                    };
                    request.Messages.Add(message);
                }
            }

            // Configure LLM settings
            if (request.Settings == null)
            {
                request.Settings = new AevatarLLMSettings();
            }

            request.Settings.ModelId = dependencies.Configuration.Model;
            request.Settings.Temperature = dependencies.Configuration.Temperature;
            request.Settings.MaxTokens = dependencies.Configuration.MaxTokens;

            // Add tool definitions if available
            if (dependencies.ToolManager != null)
            {
                var functionDefs = await dependencies.ToolManager.GenerateFunctionDefinitionsAsync(cancellationToken);
                if (functionDefs?.Count > 0)
                {
                    request.Functions = functionDefs.ToList();
                }
            }

            // Call the LLM provider
            dependencies.Logger?.LogDebug("Calling LLM provider with model: {Model}",
                request.Settings.ModelId);

            var response = await dependencies.LLMProvider.GenerateAsync(request, cancellationToken);

            // Handle tool calls if present
            if (response.AevatarFunctionCall != null && dependencies.ToolManager != null)
            {
                dependencies.Logger?.LogInformation("AI requested tool call: {ToolName}",
                    response.AevatarFunctionCall.Name);

                // Parse arguments from JSON string to dictionary
                var parameters = ToolArgumentsJson.Parse(response.AevatarFunctionCall.Arguments, dependencies.Logger);

                // Execute the tool
                var toolContext = new ToolExecutionContext
                {
                    AgentId = dependencies.AgentId,
                    ToolManager = dependencies.ToolManager,
                    PublishEventCallback = dependencies.PublishEventCallback,
                    Logger = dependencies.Logger,
                    AllowInternalTools = true,
                    AllowDangerousTools = false
                };

                var toolResult = await dependencies.ToolManager.ExecuteToolAsync(
                    response.AevatarFunctionCall.Name,
                    parameters,
                    toolContext,
                    cancellationToken);

                // Append tool result to the response
                var finalResponse =
                    $"{response.Content}\n\n[Tool Executed: {response.AevatarFunctionCall.Name}]\n{toolResult.Content}";

                dependencies.Logger?.LogDebug("Standard strategy completed with tool execution");
                return finalResponse;
            }

            // Return the LLM response directly
            dependencies.Logger?.LogDebug("Standard strategy completed successfully");
            return response.Content;
        }
        catch (Exception ex)
        {
            dependencies.Logger?.LogError(ex, "Error in Standard processing strategy");

            // Return a user-friendly error message
            return "I apologize, but I encountered an error while processing your request. Please try again.";
        }
    }

    private Aevatar.Agents.AI.AevatarChatRole ParseChatRole(string roleString)
    {
        return roleString?.ToLowerInvariant() switch
        {
            "system" => AevatarChatRole.System,
            "user" => AevatarChatRole.User,
            "assistant" => AevatarChatRole.Assistant,
            "tool" or "function" => AevatarChatRole.Tool,
            _ => AevatarChatRole.User
        };
    }

}