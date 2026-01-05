using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Observability;
using Aevatar.Agents.Core.StateProtection;
using Aevatar.Agents.Core.Telemetry;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Layer 0: Core AI Agent Base
/// - Manages LLM interactions
/// - Manages Standard State (History, Token Usage, etc)
/// - Manages Standard Config
/// </summary>
public abstract partial class AIGAgentBase : GAgentBase<AevatarAIAgentState, AevatarAIAgentConfig>
{
    #region Fields

    protected IAevatarLLMProvider? _llmProvider;
    protected bool _isInitialized;
    protected ILLMProviderFactory? LLMProviderFactory { get; set; }
    protected IAIAgentEmbeddingFactory? EmbeddingFactory { get; set; }
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private LLMProviderConfig? _activeProviderConfig;
    private readonly object _historyLock = new();
    private readonly SemaphoreSlim _historyCompactionSemaphore = new(1, 1);
    private const string HistorySummaryContextKey = "history_summary";

    #endregion

    public AIGAgentBase()
    {
    }

    public AIGAgentBase(string id) : base(id)
    {
    }

    #region Properties

    /// <summary>
    /// System prompt for the AI agent.
    /// </summary>
    public virtual string SystemPrompt { get; set; } = "You are a helpful AI assistant.";

    /// <summary>
    /// Switch (default: false):
    /// - When enabled, <see cref="ChatAsync"/> / <see cref="ChatStreamAsync"/> will append
    ///   user/assistant messages into <see cref="AevatarAIAgentState.History"/>.
    /// - When enabled, <see cref="BuildLLMRequest"/> will also replay <see cref="AevatarAIAgentState.History"/>
    ///   into <see cref="AevatarLLMRequest.Messages"/> (so the LLM becomes "stateful" across calls).
    ///
    /// NOTE:
    /// - This is intentionally OFF by default to avoid unbounded token growth and large state payloads.
    /// - Some agents (e.g. tool-aware agents) manage history on their own.
    /// </summary>
    public bool EnableChatHistoryInState { get; set; }

    /// <summary>
    /// Layer 1 + 2 (default: false):
    /// - Layer 1: Keep a sliding window of recent messages in <see cref="AevatarAIAgentState.History"/>.
    /// - Layer 2: Archive removed messages into a rolling summary stored in <see cref="AevatarAIAgentState.Context"/>
    ///   under key <c>history_summary</c>, and inject it into the next LLM requests via system prompt.
    ///
    /// NOTE:
    /// - This may trigger extra LLM calls (for summarization) when compaction happens.
    /// - If you want "LLM decides when to recall", prefer tool-based retrieval (AIGAgentBase + search_memory tool).
    /// </summary>
    public bool EnableChatHistoryCompaction { get; set; }

    /// <summary>
    /// Sliding window size for <see cref="AevatarAIAgentState.History"/> when compaction is enabled.
    /// Default: 40 messages (~20 turns).
    /// </summary>
    public int ChatHistoryMaxMessages { get; set; } = 40;

    /// <summary>
    /// Hard cap for the rolling summary stored in <c>State.Context["history_summary"]</c>.
    /// Default: 4000 characters.
    /// </summary>
    public int ChatHistorySummaryMaxChars { get; set; } = 4000;

    [field: AllowNull, MaybeNull]
    protected ConversationHistoryManager ConversationHistory =>
        field ??= CreateConversationHistoryManager();

    protected virtual ConversationHistoryManager CreateConversationHistoryManager()
    {
        return new ConversationHistoryManager(State.History);
    }

    protected virtual void AddMessageToHistory(string content, AevatarChatRole role, string? name = null)
    {
        // NOTE:
        // - Cognitive / Vote may run multiple LLM calls concurrently inside the same agent instance.
        // - RepeatedField<T> is NOT thread-safe; protect State.History modifications with a lock.
        lock (_historyLock)
        {
            ConversationHistory.AddMessage(content, role, name);
        }
    }

    protected virtual void AddMessageToHistory(AevatarChatMessage message)
    {
        lock (_historyLock)
        {
            ConversationHistory.AddMessage(message);
        }
    }

    // ============================================================
    //  History compaction (Layer 1 + Layer 2)
    // ============================================================

    protected virtual async Task CompactChatHistoryIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (!EnableChatHistoryInState || !EnableChatHistoryCompaction)
            return;

        if (ChatHistoryMaxMessages <= 0)
            return;

        int historyCount;
        lock (_historyLock)
        {
            historyCount = State.History?.Count ?? 0;
        }

        if (historyCount <= ChatHistoryMaxMessages)
            return;

        await _historyCompactionSemaphore.WaitAsync(cancellationToken);
        try
        {
            List<AevatarChatMessage> removed;
            string? existingSummary;

            lock (_historyLock)
            {
                var history = State.History;
                if (history == null)
                {
                    return;
                }

                var count = history.Count;
                if (count <= ChatHistoryMaxMessages)
                {
                    return;
                }

                var toRemove = count - ChatHistoryMaxMessages;
                removed = new List<AevatarChatMessage>(toRemove);
                for (var i = 0; i < toRemove; i++)
                {
                    removed.Add(history[i].Clone());
                }

                var kept = new List<AevatarChatMessage>(ChatHistoryMaxMessages);
                for (var i = toRemove; i < count; i++)
                {
                    kept.Add(history[i].Clone());
                }

                history.Clear();
                history.AddRange(kept);

                existingSummary = State.Context.TryGetValue(HistorySummaryContextKey, out var s) ? s : null;
            }

            if (removed.Count == 0)
            {
                return;
            }

            var updatedSummary = await UpdateHistorySummaryAsync(existingSummary, removed, cancellationToken);
            if (string.IsNullOrWhiteSpace(updatedSummary))
            {
                updatedSummary = BuildFallbackHistorySummary(existingSummary, removed);
            }

            if (ChatHistorySummaryMaxChars > 0 && updatedSummary.Length > ChatHistorySummaryMaxChars)
            {
                updatedSummary = updatedSummary[..ChatHistorySummaryMaxChars];
            }

            lock (_historyLock)
            {
                State.Context[HistorySummaryContextKey] = updatedSummary;
            }
        }
        finally
        {
            _historyCompactionSemaphore.Release();
        }
    }

    private string BuildEffectiveSystemPromptWithSummary()
    {
        var basePrompt = GetEffectiveSystemPrompt() ?? string.Empty;

        // Merge tool instructions into the system prompt (tools are always available now).
        var toolBlock = BuildToolInstructionBlock();
        var mergedPrompt = string.IsNullOrWhiteSpace(toolBlock)
            ? basePrompt
            : string.IsNullOrWhiteSpace(basePrompt)
                ? toolBlock
                : $"{basePrompt}\n\n{toolBlock}";

        if (!EnableChatHistoryInState || !EnableChatHistoryCompaction)
            return mergedPrompt;

        string? summary;
        lock (_historyLock)
        {
            summary = State.Context.TryGetValue(HistorySummaryContextKey, out var s) ? s : null;
        }

        if (string.IsNullOrWhiteSpace(summary))
            return mergedPrompt;

        // Keep it explicit and stable; avoid fancy formatting that the model may misinterpret.
        return $"{mergedPrompt}\n\nConversation summary (memory):\n{summary}\n";
    }

    protected virtual async Task<string?> UpdateHistorySummaryAsync(
        string? existingSummary,
        IReadOnlyList<AevatarChatMessage> newlyArchivedMessages,
        CancellationToken cancellationToken)
    {
        try
        {
            var transcript = BuildTranscriptForSummarization(newlyArchivedMessages, maxChars: 12000);

            var prev = string.IsNullOrWhiteSpace(existingSummary)
                ? "(none)"
                : existingSummary.Trim();

            var prompt = $"""
You maintain a compact, factual memory summary of a conversation.

Existing summary:
{prev}

New conversation messages to merge:
{transcript}

Update the summary. Rules:
- Be factual. Do NOT invent.
- Keep it concise.
- Use this structure exactly:

Facts:
- ...

Decisions:
- ...

Constraints:
- ...

Open questions:
- ...
""";

            var modelId = !string.IsNullOrWhiteSpace(Config.Model)
                ? Config.Model
                : AevatarAIDefaults.DefaultModel;

            var summarizeRequest = new AevatarLLMRequest
            {
                SystemPrompt = "You are a precise conversation memory summarizer.",
                UserPrompt = prompt,
                Settings = new AevatarLLMSettings
                {
                    ModelId = modelId,
                    Temperature = 0,
                    MaxTokens = 512
                }
            };

            var response = await LLMProvider.GenerateAsync(summarizeRequest, cancellationToken);
            return response.Content?.Trim();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "History summarization failed; falling back to heuristic summary.");
            return null;
        }
    }

    private static string BuildTranscriptForSummarization(
        IReadOnlyList<AevatarChatMessage> messages,
        int maxChars)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var m in messages)
        {
            var role = m.Role.ToString().ToLowerInvariant();
            var content = (m.Content ?? string.Empty).Replace("\r", "").Trim();
            if (content.Length > 800)
                content = content[..800] + "…";

            sb.Append(role);
            sb.Append(": ");
            sb.AppendLine(content);
        }

        var text = sb.ToString().Trim();
        if (maxChars > 0 && text.Length > maxChars)
        {
            // Keep the tail of archived chunk (usually more relevant than the beginning of the chunk).
            text = text[^maxChars..];
        }

        return text;
    }

    private static string BuildFallbackHistorySummary(
        string? existingSummary,
        IReadOnlyList<AevatarChatMessage> archived)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(existingSummary))
        {
            sb.AppendLine(existingSummary.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Archived notes:");

        var take = Math.Min(archived.Count, 8);
        var start = Math.Max(0, archived.Count - take);
        for (var i = start; i < archived.Count; i++)
        {
            var m = archived[i];
            var role = m.Role.ToString().ToLowerInvariant();
            var content = (m.Content ?? string.Empty).Replace("\r", "").Trim();
            if (content.Length > 200)
                content = content[..200] + "…";

            sb.Append("- ");
            sb.Append(role);
            sb.Append(": ");
            sb.AppendLine(content);
        }

        if (archived.Count > take)
        {
            sb.AppendLine($"- ... ({archived.Count - take} more)");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Gets the LLM provider.
    /// </summary>
    public IAevatarLLMProvider LLMProvider
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "AI Agent must be initialized before use. Call InitializeAsync() first.");
            return _llmProvider!;
        }
    }

    protected bool HasEmbeddingGenerator => _embeddingGenerator != null;
    protected LLMProviderConfig? ActiveProviderConfig => _activeProviderConfig;

    protected IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator =>
        _embeddingGenerator ?? throw new InvalidOperationException(
            "Embedding generator is not configured. Ensure LLM provider Embeddings settings are provided and IAIAgentEmbeddingFactory is registered.");

    #endregion

    #region Initialization

    /// <summary>
    /// Helper method to load and configure state and configuration during initialization.
    /// Must be called within an InitializationScope.
    /// </summary>
    protected virtual async Task InitializeStateAndConfigAsync(
        Action<AevatarAIAgentConfig>? configAI,
        CancellationToken cancellationToken)
    {
        // NOTE: Do NOT call ActivateAsync() here!
        // This method is typically called from OnActivateAsync, 
        // calling ActivateAsync again would cause infinite recursion.

        // Load state and config if stores are available
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, cancellationToken) ?? new AevatarAIAgentState();
        }

        var agentType = GetType();
        if (ConfigStore != null)
        {
            Config = await ConfigStore.LoadAsync(agentType, Id, cancellationToken) ?? new AevatarAIAgentConfig();
        }

        // Configure AI settings
        ConfigAI(Config);
        configAI?.Invoke(Config);

        if (ConfigStore != null)
        {
            await ConfigStore.SaveAsync(agentType, Id, Config, cancellationToken);
        }
    }

    /// <summary>
    /// Initialize the AI agent with a named LLM provider from ASP.NET Options.
    /// This method must be called before using the agent.
    /// </summary>
    /// <param name="providerName">Name of the LLM provider from appsettings.json (e.g., "openai-gpt4", "azure-gpt35")</param>
    /// <param name="configAI">Optional configuration action for AI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task InitializeAsync(
        string providerName,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        // Use InitializationScope to allow State and Config modifications during initialization
        using var initScope = StateProtectionContext.BeginInitializationScope();

        await InitializeStateAndConfigAsync(configAI, cancellationToken);

        var providerFactory = RequireLLMProviderFactory();
        _activeProviderConfig = providerFactory.GetProviderConfig(providerName);

        // Create LLM Provider from factory using provider name
        _llmProvider = await CreateLLMProviderFromFactoryAsync(providerName, cancellationToken);

        await InitializeEmbeddingGeneratorAsync(_activeProviderConfig, cancellationToken);

        // Tool system is now part of the core base: every AI agent is tool-capable.
        await InitializeToolsAsync(cancellationToken);

        _isInitialized = true;

        Logger.LogInformation("AI Agent {AgentId} initialized with LLM provider '{ProviderName}'", Id, providerName);
    }

    /// <summary>
    /// Initialize the AI agent with custom LLM provider configuration.
    /// This method must be called before using the agent.
    /// </summary>
    /// <param name="providerConfig">Custom LLM provider configuration</param>
    /// <param name="configAI">Optional configuration action for AI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task InitializeAsync(
        LLMProviderConfig providerConfig,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        // Use InitializationScope to allow State and Config modifications during initialization
        using var initScope = StateProtectionContext.BeginInitializationScope();

        await InitializeStateAndConfigAsync(configAI, cancellationToken);

        _activeProviderConfig = providerConfig;

        // Create LLM Provider from custom config
        _llmProvider = await CreateLLMProviderFromConfigAsync(providerConfig, cancellationToken);

        await InitializeEmbeddingGeneratorAsync(_activeProviderConfig, cancellationToken);

        // Tool system is now part of the core base: every AI agent is tool-capable.
        await InitializeToolsAsync(cancellationToken);

        _isInitialized = true;

        Logger.LogInformation("AI Agent {AgentId} initialized with custom LLM provider '{ProviderType}'",
            Id, providerConfig.ProviderType);
    }

    #endregion

    #region LLM Provider Creation

    /// <summary>
    /// Creates LLM Provider from factory using provider name.
    /// </summary>
    protected virtual async Task<IAevatarLLMProvider> CreateLLMProviderFromFactoryAsync(
        string providerName,
        CancellationToken cancellationToken)
    {
        // Get provider from factory
        return await RequireLLMProviderFactory().GetProviderAsync(providerName, cancellationToken);
    }

    /// <summary>
    /// Creates LLM Provider from custom configuration.
    /// </summary>
    protected virtual async Task<IAevatarLLMProvider> CreateLLMProviderFromConfigAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken)
    {
        // Create provider from config using factory
        return RequireLLMProviderFactory().CreateProvider(providerConfig, cancellationToken);
    }

    protected ILLMProviderFactory RequireLLMProviderFactory()
    {
        return LLMProviderFactory ?? throw new InvalidOperationException(
            "ILLMProviderFactory is not available. " +
            "Ensure DI is configured and AIAgentLLMProviderFactoryInjector runs after activation, " +
            "or override provider creation in a derived agent.");
    }

    #endregion

    #region Embeddings

    protected bool TryGetEmbeddingGenerator(
        [NotNullWhen(true)] out IEmbeddingGenerator<string, Embedding<float>>? generator)
    {
        generator = _embeddingGenerator;
        return generator != null;
    }

    protected virtual async Task InitializeEmbeddingGeneratorAsync(
        LLMProviderConfig? providerConfig,
        CancellationToken cancellationToken)
    {
        if (providerConfig is not { Embeddings.Enabled: true })
        {
            return;
        }

        if (EmbeddingFactory == null)
        {
            Logger.LogDebug("EmbeddingFactory not available, skipping embedding initialization for provider {Provider}",
                providerConfig.Name);
            return;
        }

        try
        {
            var generator = await EmbeddingFactory.CreateAsync(providerConfig, cancellationToken);
            if (generator != null)
            {
                _embeddingGenerator = generator;
                Logger.LogInformation("Embedding generator initialized for provider {Provider}", providerConfig.Name);
            }
            else
            {
                Logger.LogDebug("EmbeddingFactory returned null for provider {Provider}", providerConfig.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to initialize embedding generator for provider {Provider}", providerConfig.Name);
        }
    }

    protected virtual async Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_embeddingGenerator == null)
            throw new InvalidOperationException(
                "Embedding generator is not configured. Ensure Embeddings settings exist in the provider configuration.");

        var generationOptions = options ?? BuildDefaultEmbeddingOptions();
        var embeddings = await _embeddingGenerator.GenerateAsync(inputs, generationOptions, cancellationToken);
        return embeddings;
    }

    protected virtual async Task<Embedding<float>?> GenerateEmbeddingAsync(
        string input,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await GenerateEmbeddingsAsync(new[] { input }, options, cancellationToken);
        return embeddings.Count > 0 ? embeddings[0] : null;
    }

    protected virtual EmbeddingGenerationOptions BuildDefaultEmbeddingOptions()
    {
        var options = new EmbeddingGenerationOptions();
        if (_activeProviderConfig?.Embeddings != null)
        {
            if (!string.IsNullOrWhiteSpace(_activeProviderConfig.Embeddings.Model))
            {
                options.ModelId = _activeProviderConfig.Embeddings.Model;
            }
            else if (!string.IsNullOrWhiteSpace(_activeProviderConfig.Model))
            {
                options.ModelId = _activeProviderConfig.Model;
            }

            if (_activeProviderConfig.Embeddings.Dimensions.HasValue)
            {
                options.Dimensions = _activeProviderConfig.Embeddings.Dimensions;
            }
        }
        else if (!string.IsNullOrWhiteSpace(_activeProviderConfig?.Model))
        {
            options.ModelId = _activeProviderConfig.Model;
        }

        return options;
    }

    protected static double CosineSimilarity(Embedding<float> left, Embedding<float> right)
    {
        var leftSpan = left.Vector.Span;
        var rightSpan = right.Vector.Span;

        if (leftSpan.Length != rightSpan.Length)
            throw new InvalidOperationException("Embedding dimensions must match to calculate cosine similarity.");

        double dot = 0;
        double magLeft = 0;
        double magRight = 0;

        for (var i = 0; i < leftSpan.Length; i++)
        {
            var l = leftSpan[i];
            var r = rightSpan[i];
            dot += l * r;
            magLeft += l * l;
            magRight += r * r;
        }

        if (magLeft == 0 || magRight == 0)
            return 0;

        return dot / (Math.Sqrt(magLeft) * Math.Sqrt(magRight));
    }

    #endregion

    #region Configuration Methods

    /// <summary>
    /// Configure AI settings. Override in derived classes.
    /// </summary>
    protected virtual void ConfigAI(AevatarAIAgentConfig config)
    {
        // Set defaults from centralized constants
        config.Model = AevatarAIDefaults.DefaultModel;
        config.Temperature = AevatarAIDefaults.DefaultTemperature;
        config.MaxOutputTokens = AevatarAIDefaults.DefaultMaxOutputTokens;

        // Override in derived classes
    }

    #endregion

    #region Chat Methods

    /// <summary>
    /// Process a chat request and return a response.
    /// </summary>
    /// <param name="request">Chat request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chat response</returns>
    public virtual async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        var provider = _activeProviderConfig?.ProviderType ?? "unknown";
        var model = Config.Model ?? "default";
        var stopwatch = Stopwatch.StartNew();

        // Start LLM Telemetry
        using var activity = LLMTelemetry.StartLLMCall(Id, provider, model, isStreaming: false);
        using var logScope = LoggingScope.CreateLLMCallScope(Logger, Id, provider, model);

        AgentLogMessages.LLMCallStarting(Logger, Id, provider, model);

        try
        {
            // Keep history bounded before building request (prevents token blow-up).
            await CompactChatHistoryIfNeededAsync(cancellationToken);

            // Ensure built-in tools are registered and cached.
            await InitializeToolsAsync(cancellationToken);

            // Build LLM request from chat request
            var llmRequest = BuildLLMRequest(request);

            // Optional: persist conversation to State.History (default off)
            if (EnableChatHistoryInState)
            {
                AddMessageToHistory(request.Message, AevatarChatRole.User);
            }

            // Optional: persist conversation to MemoryStore (default off, best-effort)
            await AppendChatMemoryAsync(AevatarChatRole.User, request.Message ?? string.Empty, request, cancellationToken);

            // Call LLM
            var llmResponse = await LLMProvider.GenerateAsync(llmRequest, cancellationToken);
            ToolCallInfo? toolCall = null;

            // Tool/function calling loop
            if (llmResponse.AevatarFunctionCall != null)
            {
                var (finalResponse, lastToolCall) = await ExecuteToolCallLoopAsync(
                    request,
                    llmRequest,
                    llmResponse,
                    cancellationToken);
                llmResponse = finalResponse;
                toolCall = lastToolCall;
            }

            stopwatch.Stop();

            // Build chat response
            var response = new ChatResponse
            {
                Content = llmResponse.Content,
                RequestId = request.RequestId
            };

            if (toolCall != null)
            {
                response.ToolCalled = true;
                response.ToolCall = toolCall;
            }

            if (EnableChatHistoryInState && !string.IsNullOrEmpty(response.Content))
            {
                AddMessageToHistory(response.Content, AevatarChatRole.Assistant);
            }

            // Optional: persist assistant output to MemoryStore (default off, best-effort)
            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                await AppendChatMemoryAsync(AevatarChatRole.Assistant, response.Content!, request, cancellationToken);
            }

            // Compact again after appending new messages (keeps state bounded for next call).
            await CompactChatHistoryIfNeededAsync(cancellationToken);

            // Add token usage if available
            var promptTokens = 0;
            var completionTokens = 0;
            if (llmResponse.Usage != null)
            {
                promptTokens = llmResponse.Usage.PromptTokens;
                completionTokens = llmResponse.Usage.CompletionTokens;

                response.Usage = new AevatarTokenUsage
                {
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = llmResponse.Usage.TotalTokens
                };
            }

            // Record LLM Telemetry
            LLMTelemetry.RecordLLMCallCompleted(
                activity, provider, model, stopwatch.ElapsedMilliseconds,
                promptTokens, completionTokens,
                promptChars: request.Message?.Length ?? 0,
                responseChars: response.Content?.Length ?? 0);

            AgentLogMessages.LLMCallCompleted(Logger, Id, promptTokens, completionTokens,
                stopwatch.ElapsedMilliseconds);

            // Publish chat response event
            await PublishAsync(new ChatResponseEvent
            {
                RequestId = request.RequestId,
                Content = response.Content,
                TokensUsed = response.Usage?.TotalTokens ?? 0,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }, ct: cancellationToken);

            // Record the AI decision as an event (Event Sourcing)
            RaiseAIDecision(
                request.Message ?? string.Empty,
                response.Content ?? string.Empty,
                response.Usage?.TotalTokens ?? 0,
                new Dictionary<string, string>
                {
                    ["request_id"] = request.RequestId,
                    ["chat_type"] = toolCall != null ? "tool_execution" : "sync"
                });

            // Auto-confirm if configured and EventStore is present
            if (AutoConfirmEvents && EventStore != null)
            {
                await ConfirmEventsAsync(cancellationToken);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorType = ex.GetType().Name;

            LLMTelemetry.RecordLLMCallFailed(activity, provider, model, errorType, ex.Message,
                stopwatch.ElapsedMilliseconds);
            AgentLogMessages.LLMCallFailed(Logger, Id, errorType, ex.Message, ex);

            throw;
        }
    }

    /// <summary>
    /// Build LLM request from chat request.
    /// </summary>
    protected virtual AevatarLLMRequest BuildLLMRequest(
        ChatRequest request)
    {
        var settings = GetLLMSettings(request);
        if (request.StopSequences.Count > 0)
        {
            settings.StopSequences = request.StopSequences.ToList();
        }

        // Build message list:
        // - Default (history disabled): only include current user message.
        // - History enabled: include previous State.History + current user message.
        var messages = new List<AevatarChatMessage>();
        if (EnableChatHistoryInState)
        {
            lock (_historyLock)
            {
                if (State.History != null && State.History.Count > 0)
                {
                    foreach (var msg in State.History)
                    {
                        messages.Add(msg);
                    }
                }
            }
        }

        messages.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.User,
            Content = request.Message
        });

        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = BuildEffectiveSystemPromptWithSummary(),
            Messages = messages,
            Settings = settings
        };

        AttachToolsToRequest(llmRequest);

        if (!string.IsNullOrWhiteSpace(request.StageHint))
        {
            llmRequest.Context = new Dictionary<string, object>
            {
                ["stage_hint"] = request.StageHint!
            };
        }

        return llmRequest;
    }

    /// <summary>
    /// Determine the effective system prompt, preferring configuration override.
    /// </summary>
    protected virtual string? GetEffectiveSystemPrompt()
    {
        return !string.IsNullOrWhiteSpace(Config.SystemPrompt)
            ? Config.SystemPrompt
            : SystemPrompt;
    }

    /// <summary>
    /// Get LLM settings from chat request.
    /// </summary>
    protected virtual AevatarLLMSettings GetLLMSettings(ChatRequest request)
    {
        // Use request values if provided (considering 0 as a valid temperature), otherwise use configuration
        // For temperature: accept any value >= 0 as valid override
        // For maxTokens: only positive values are valid overrides
        var temperature = request.Temperature >= 0 ? request.Temperature : Config.Temperature;
        var maxTokens = request.MaxTokens > 0 ? request.MaxTokens : Config.MaxOutputTokens;

        return new AevatarLLMSettings
        {
            Temperature = temperature,
            MaxTokens = maxTokens,
            ModelId = Config.Model
        };
    }

    /// <summary>
    /// Create a chat request with the given message.
    /// </summary>
    public virtual ChatRequest CreateChatRequest(string message)
    {
        return ChatRequest.Create(message);
    }

    /// <summary>
    /// Generate a response to a message (convenience method).
    /// </summary>
    public virtual Task<ChatResponse> GenerateResponseAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(message);
        return ChatAsync(request, cancellationToken);
    }

    /// <summary>
    /// Generate a streaming response to a chat request.
    /// </summary>
    public virtual async IAsyncEnumerable<string> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        // Keep history bounded before building request (prevents token blow-up).
        await CompactChatHistoryIfNeededAsync(cancellationToken);

        // Ensure built-in tools are registered and cached.
        await InitializeToolsAsync(cancellationToken);

        // Build LLM request
        var llmRequest = BuildLLMRequest(request);

        // Optional: persist the user message (default off)
        if (EnableChatHistoryInState)
        {
            AddMessageToHistory(request.Message, AevatarChatRole.User);
        }

        // Optional: persist conversation to MemoryStore (default off, best-effort)
        await AppendChatMemoryAsync(AevatarChatRole.User, request.Message ?? string.Empty, request, cancellationToken);

        // Stream from LLM
        var enumerator = LLMProvider.GenerateStreamAsync(llmRequest, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        var assistantBuffer = EnableChatHistoryInState
            ? new System.Text.StringBuilder()
            : null;
        var reasoningBuffer = EnableChatHistoryInState
            ? new System.Text.StringBuilder()
            : null;
        var completedSuccessfully = false;

        try
        {
            while (true)
            {
                AevatarLLMToken token;
                try
                {
                    var hasNext = await enumerator.MoveNextAsync();
                    if (!hasNext) break;
                    token = enumerator.Current;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in streaming chat request {RequestId}", request.RequestId);
                    throw;
                }

                // Streaming + tools:
                // - If the model returns a function call mid-stream, execute tools non-streaming and
                //   emit the final answer as a single chunk (best-effort).
                if (token.AevatarFunctionCall != null)
                {
                    var toolCallResponse = new AevatarLLMResponse
                    {
                        AevatarFunctionCall = token.AevatarFunctionCall,
                        // Capture any reasoning accumulated so far for the history/logging if needed
                        Content = assistantBuffer?.ToString() ?? string.Empty 
                    };
                    
                    var accumulatedReasoning = reasoningBuffer?.ToString();
                    if (!string.IsNullOrEmpty(accumulatedReasoning))
                    {
                        toolCallResponse.Metadata ??= new Dictionary<string, object>();
                        toolCallResponse.Metadata["reasoning_content"] = accumulatedReasoning;
                    }

                    var (finalResponse, _) = await ExecuteToolCallLoopAsync(
                        request,
                        llmRequest,
                        toolCallResponse,
                        cancellationToken);

                    var finalText = finalResponse.Content ?? string.Empty;
                    if (!string.IsNullOrEmpty(finalText))
                    {
                        assistantBuffer?.Append(finalText);
                        yield return finalText;
                    }

                    completedSuccessfully = true;
                    break;
                }

                var content = token.Content;
                var reasoning = token.ReasoningContent;
                var isComplete = token.IsComplete;

                if (!string.IsNullOrEmpty(reasoning))
                {
                    reasoningBuffer?.Append(reasoning);
                }

                if (!string.IsNullOrEmpty(content))
                {
                    assistantBuffer?.Append(content);
                    yield return content;
                }

                if (isComplete)
                    break;
            }

            completedSuccessfully = true;
        }
        finally
        {
            await enumerator.DisposeAsync();

            // Persist assistant message only if stream completed successfully
            if (EnableChatHistoryInState && completedSuccessfully)
            {
                var assistantText = assistantBuffer?.ToString() ?? string.Empty;
                var finalReasoning = reasoningBuffer?.ToString();

                if (!string.IsNullOrEmpty(assistantText) || !string.IsNullOrEmpty(finalReasoning))
                {
                    var msg = new AevatarChatMessage
                    {
                         Role = AevatarChatRole.Assistant,
                         Content = assistantText,
                         Timestamp = TimestampHelper.GetUtcNow()
                    };
                    
                    if (!string.IsNullOrEmpty(finalReasoning)) 
                    {
                        msg.Metadata.Add("reasoning_content", finalReasoning);
                    }

                    AddMessageToHistory(msg);
                }
            }

            // Optional: persist assistant output to MemoryStore (default off, best-effort)
            if (completedSuccessfully)
            {
                var assistantText = assistantBuffer?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(assistantText))
                {
                    await AppendChatMemoryAsync(AevatarChatRole.Assistant, assistantText, request, cancellationToken);
                }
            }

            // Compact after streaming finishes (keeps state bounded for next call).
            await CompactChatHistoryIfNeededAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Generate a streaming response to a message (convenience method).
    /// </summary>
    public virtual IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(message);
        return ChatStreamAsync(request, cancellationToken);
    }

    /// <summary>
    /// Check if the LLM provider supports streaming.
    /// </summary>
    public virtual async Task<bool> SupportsStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            return false;

        var modelInfo = await LLMProvider.GetModelInfoAsync(cancellationToken);
        return modelInfo.SupportsStreaming;
    }

    #endregion

    #region AI Event Sourcing Support

    /// <summary>
    /// Auto-confirm events after AI operations.
    /// Defaults to false. Override to enable.
    /// </summary>
    protected virtual bool AutoConfirmEvents => false;

    /// <summary>
    /// Record an AI decision as an event.
    /// </summary>
    protected void RaiseAIDecision(
        string prompt,
        string response,
        int tokensUsed,
        Dictionary<string, string>? metadata = null)
    {
        var aiEvent = new AIDecisionEvent
        {
            Prompt = prompt,
            Response = response,
            TokensUsed = tokensUsed,
            Model = Config.Model,
            Temperature = Config.Temperature,
            Timestamp = TimestampHelper.GetUtcNow()
        };

        // Add AI-specific metadata
        var eventMetadata = metadata ?? new Dictionary<string, string>();
        eventMetadata["ai_model"] = Config.Model;
        eventMetadata["ai_temperature"] = Config.Temperature.ToString(CultureInfo.InvariantCulture);

        RaiseEvent(aiEvent, eventMetadata);
    }

    /// <summary>
    /// Pure functional state transition for AI Agent.
    /// </summary>
    protected override void TransitionState(AevatarAIAgentState state, IMessage evt)
    {
        // Default implementation handles standard AI events
        // Users can override to handle custom events
        switch (evt)
        {
            case AIDecisionEvent aiEvent:
                // Update state with AI decision if needed
                // For now, standard state might not need to track every decision,
                // but we can add it to history if we want.
                // The standard AevatarAIAgentState might have a history field.
                break;
            case ChatResponseEvent chatEvent:
                // Already handled by ChatAsync return value, but maybe we want to update history here?
                break;
        }
    }

    #endregion
}