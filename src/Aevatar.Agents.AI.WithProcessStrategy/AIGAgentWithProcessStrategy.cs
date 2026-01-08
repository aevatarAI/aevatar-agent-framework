using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithProcessStrategy.Abstractions;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;
using Aevatar.Agents.AI.WithProcessStrategy.Strategies;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithProcessStrategy;

/// <summary>
/// Strategy decision from LLM meta-reasoning
/// </summary>
public class StrategyDecision
{
    public string Strategy { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Level 3: AI Agent with advanced processing strategies.
/// Builds on AIGAgentBase (which already has built-in tool capabilities) by adding functionality for LLM strategic responses.
/// Supports multiple processing strategies (e.g., Chain-of-Thought, ReAct, Tree-of-Thoughts, etc.)
/// </summary>
/// <typeparam name="TState">The agent state type (must be Protobuf)</typeparam>
public abstract class AIGAgentWithProcessStrategy<TState> : AIGAgentBase<TState>
    where TState : class, Google.Protobuf.IMessage<TState>, new()
{
    #region Fields

    private readonly Dictionary<string, IAevatarAIProcessingStrategy> _strategies;
    private IAevatarAIProcessingStrategyFactory? _strategyFactory;

    /// <summary>
    /// Meta-reasoning prompt template for strategy selection
    /// </summary>
    private const string _metaReasoningPromptTemplate = @"
You are an AI strategy selector. Your task is to analyze the user's request and select the most appropriate processing strategy.

Available Strategies:
standard: Use for simple, direct questions that don't require deep reasoning or tools
chain_of_thought: Use when the user asks for step-by-step reasoning, explanations, or the problem requires logical deduction
react: Use when the request requires using tools, executing functions, or accessing external data
tree_of_thoughts: Use for complex problems that require exploring multiple approaches or when creative problem-solving is needed

Guidelines:
1. If the user wants a simple answer without explanation → standard
2. If the user asks 'how', 'why', 'explain', or 'step by step' → chain_of_thought
3. If the task involves calculation, searching, querying data, or executing operations → react
4. If the problem is complex, has multiple solutions, or requires analysis → tree_of_thoughts

Available Tools: {{TOOLS}}

Analyze this user request and return a JSON object with the following structure:
{
  ""strategy"": ""standard|chain_of_thought|react|tree_of_thoughts"",
  ""confidence"": 0.0-1.0,
  ""reasoning"": ""Brief explanation of why this strategy was chosen""
}

User Request: {{USER_REQUEST}}

Strategy Decision:";

    #endregion

    #region Properties

    /// <summary>
    /// Gets available processing strategies.
    /// </summary>
    protected IReadOnlyDictionary<string, IAevatarAIProcessingStrategy> Strategies => _strategies;

    /// <summary>
    /// Gets the strategy factory used to create strategies.
    /// </summary>
    protected IAevatarAIProcessingStrategyFactory StrategyFactory
    {
        get
        {
            EnsureStrategyFactoryInitialized();
            return _strategyFactory!;
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the AIGAgentWithProcessStrategy class.
    /// </summary>
    protected AIGAgentWithProcessStrategy() : base()
    {
        _strategies = new Dictionary<string, IAevatarAIProcessingStrategy>();
    }

    /// <summary>
    /// Initializes a new instance with dependency injection.
    /// </summary>
    protected AIGAgentWithProcessStrategy(
        IAevatarAIProcessingStrategyFactory strategyFactory)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _strategies = InitializeStrategies();
    }

    #endregion

    #region Strategy Management

    /// <summary>
    /// Initialize processing strategies.
    /// </summary>
    protected virtual Dictionary<string, IAevatarAIProcessingStrategy> InitializeStrategies()
    {
        EnsureStrategyFactoryInitialized();

        return new Dictionary<string, IAevatarAIProcessingStrategy>
        {
            ["standard"] = StrategyFactory.GetOrCreateStrategy(AevatarAIProcessingMode.Standard),
            ["chain_of_thought"] = StrategyFactory.GetOrCreateStrategy(AevatarAIProcessingMode.ChainOfThought),
            ["react"] = StrategyFactory.GetOrCreateStrategy(AevatarAIProcessingMode.ReAct),
            ["tree_of_thoughts"] = StrategyFactory.GetOrCreateStrategy(AevatarAIProcessingMode.TreeOfThoughts)
        };
    }

    /// <summary>
    /// Ensure strategy factory is initialized.
    /// </summary>
    private void EnsureStrategyFactoryInitialized()
    {
        if (_strategyFactory != null)
            return;

        _strategyFactory = CreateStrategyFactory();

        var strategies = InitializeStrategies();
        foreach (var strategy in strategies)
        {
            _strategies[strategy.Key] = strategy.Value;
        }
    }

    /// <summary>
    /// Create the strategy factory. Override to customize.
    /// </summary>
    protected virtual IAevatarAIProcessingStrategyFactory CreateStrategyFactory()
    {
        return new AevatarAIProcessingStrategyFactory(null, null);
    }

    /// <summary>
    /// Select the appropriate strategy for a request using LLM meta-reasoning.
    /// </summary>
    protected virtual async Task<string> SelectStrategyAsync(Aevatar.Agents.AI.ChatRequest request)
    {
        if (request.Context?.ContainsKey("strategy") == true)
        {
            var requestedStrategy = request.Context["strategy"]?.ToString();
            if (!string.IsNullOrEmpty(requestedStrategy) && _strategies.ContainsKey(requestedStrategy))
            {
                Logger?.LogDebug("Strategy specified in context: {Strategy}", requestedStrategy);
                return requestedStrategy;
            }
        }

        try
        {
            var availableTools = await HasToolsAsync() ? await GetRegisteredToolsAsync() : new List<ToolDefinition>();
            var toolNames = availableTools.Any() ? string.Join(", ", availableTools.Select(t => t.Name)) : "none";

            var metaPrompt = _metaReasoningPromptTemplate
                .Replace("{{TOOLS}}", toolNames)
                .Replace("{{USER_REQUEST}}", request.Message);

            Logger?.LogDebug("Requesting LLM for strategy selection...");

            var metaRequest = new AevatarLLMRequest
            {
                SystemPrompt = "You are a strategy selection assistant. Always respond with valid JSON only.",
                Messages = new List<AevatarChatMessage>
                {
                    new()
                    {
                        Role = AevatarChatRole.User,
                        Content = metaPrompt
                    }
                },
                Settings = new AevatarLLMSettings
                {
                    Temperature = 0.1,
                    MaxTokens = 500
                }
            };

            var metaResponse = await LLMProvider.GenerateAsync(metaRequest, CancellationToken.None);

            var decision = ParseStrategyDecision(metaResponse.Content);

            Logger?.LogInformation("LLM selected strategy: {Strategy} (confidence: {Confidence:F2}, reasoning: {Reasoning})",
                decision.Strategy, decision.Confidence, decision.Reasoning);

            return decision.Strategy;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to use meta-reasoning for strategy selection, using default strategy");
            return "standard";
        }
    }


    /// <summary>
    /// Parse strategy decision from LLM JSON response.
    /// </summary>
    private StrategyDecision ParseStrategyDecision(string jsonResponse)
    {
        var jsonStart = jsonResponse.IndexOf('{');
        var jsonEnd = jsonResponse.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            jsonResponse = jsonResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        try
        {
            return JsonSerializer.Deserialize<StrategyDecision>(jsonResponse) ??
                new StrategyDecision { Strategy = "standard", Confidence = 0.5, Reasoning = "Failed to parse JSON, using default" };
        }
        catch (JsonException ex)
        {
            Logger?.LogWarning(ex, "Failed to parse strategy decision JSON: {Json}", jsonResponse);
            return new StrategyDecision { Strategy = "standard", Confidence = 0.5, Reasoning = "Invalid JSON, using default" };
        }
    }

    #endregion
}