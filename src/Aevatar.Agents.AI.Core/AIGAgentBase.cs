using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.StateProtection;
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

    private const string NotInitializedExceptionMessage =
        "AI Agent must be initialized before use. Call InitializeAsync() first.";

    protected IAevatarLLMProvider? _llmProvider;
    protected bool _isInitialized;
    protected ILLMProviderFactory? LLMProviderFactory { get; set; }
    protected IAIAgentEmbeddingFactory? EmbeddingFactory { get; set; }
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private LLMProviderConfig? _activeProviderConfig;

    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);

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
    /// Gets the LLM provider.
    /// </summary>
    public IAevatarLLMProvider LLMProvider
    {
        get
        {
            EnsureInitialized();
            return _llmProvider!;
        }
    }

    protected bool HasEmbeddingGenerator => _embeddingGenerator != null;
    protected LLMProviderConfig? ActiveProviderConfig => _activeProviderConfig;

    protected IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator =>
        _embeddingGenerator ?? throw new InvalidOperationException(
            "Embedding generator is not configured. Ensure LLM provider Embeddings settings are provided and IAIAgentEmbeddingFactory is registered.");

    /// <summary>
    /// Internal logger access for extracted runtime components (e.g. AgentSkillsRuntime).
    /// Keep it internal to avoid widening the public surface area.
    /// </summary>
    internal ILogger InternalLogger => Logger;

    // ------------------------------------------------------------
    // Internal wrappers for extracted runtime components
    // ------------------------------------------------------------

    internal LLMProviderConfig? InternalActiveProviderConfig => ActiveProviderConfig;

    internal bool InternalTryGetEmbeddingGenerator(
        [NotNullWhen(true)] out IEmbeddingGenerator<string, Embedding<float>>? generator)
        => TryGetEmbeddingGenerator(out generator);

    internal EmbeddingGenerationOptions InternalBuildDefaultEmbeddingOptions()
        => BuildDefaultEmbeddingOptions();

    internal Task<Embedding<float>?> InternalGenerateEmbeddingAsync(
        string input,
        CancellationToken cancellationToken)
        => GenerateEmbeddingAsync(input, cancellationToken: cancellationToken);

    internal IAevatarToolManager InternalToolManager => ToolManager;

    internal Task InternalInitializeToolsAsync(CancellationToken ct) => InitializeToolsAsync(ct);

    internal Task InternalRefreshToolCachesAsync(CancellationToken ct) => RefreshToolCachesAsync(ct);

    // ------------------------------------------------------------
    // Internal config access for in-assembly helpers (e.g. YAML appliers)
    // ------------------------------------------------------------
    internal AevatarAIAgentConfig InternalConfig => Config;

    #endregion

    #region Initialization

    protected void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        throw new InvalidOperationException(NotInitializedExceptionMessage);
    }

    protected (string ProviderType, string ModelId) GetProviderAndModelForTelemetry()
    {
        var provider = _activeProviderConfig?.ProviderType ?? "unknown";
        var model = !string.IsNullOrWhiteSpace(Config.Model)
            ? Config.Model
            : AevatarAIDefaults.DefaultModel;
        return (provider, model);
    }

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
        // NOTE: When event sourcing is active (Version > 0), avoid direct State assignment.
        if (StateStore != null && GetCurrentVersion() == 0)
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
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        await InitializeAsyncCore(
            createProvider: async ct =>
            {
                var providerFactory = RequireLLMProviderFactory();
                var cfg = providerFactory.GetProviderConfig(providerName);
                var provider = await CreateLLMProviderFromFactoryAsync(providerName, ct);
                return (Provider: provider, ProviderConfig: cfg, ProviderForLog: providerName);
            },
            configAI,
            cancellationToken);
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
        ArgumentNullException.ThrowIfNull(providerConfig);

        await InitializeAsyncCore(
            createProvider: async ct =>
            {
                var provider = await CreateLLMProviderFromConfigAsync(providerConfig, ct);
                var providerForLog = string.IsNullOrWhiteSpace(providerConfig.ProviderType)
                    ? "custom"
                    : providerConfig.ProviderType;
                return (Provider: provider, ProviderConfig: providerConfig, ProviderForLog: providerForLog);
            },
            configAI,
            cancellationToken);
    }

    private async Task InitializeAsyncCore(
        Func<CancellationToken, Task<(IAevatarLLMProvider Provider, LLMProviderConfig? ProviderConfig, string ProviderForLog)>>
            createProvider,
        Action<AevatarAIAgentConfig>? configAI,
        CancellationToken cancellationToken)
    {
        if (_isInitialized)
            return;

        await _initializationSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
                return;

            // Use InitializationScope to allow State and Config modifications during initialization.
            using var initScope = StateProtectionContext.BeginInitializationScope();

            await InitializeStateAndConfigAsync(configAI, cancellationToken);

            var (provider, providerConfig, providerForLog) = await createProvider(cancellationToken);
            _activeProviderConfig = providerConfig;
            _llmProvider = provider;

            await InitializeEmbeddingGeneratorAsync(_activeProviderConfig, cancellationToken);

            // Tool system is now part of the core base: every AI agent is tool-capable.
            await InitializeToolsAsync(cancellationToken);

            _isInitialized = true;

            Logger.LogInformation("AI Agent {AgentId} initialized with LLM provider '{ProviderName}'", Id, providerForLog);
        }
        finally
        {
            _initializationSemaphore.Release();
        }
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
        if (providerConfig?.Embeddings == null)
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
            Logger.LogWarning(ex, "Failed to initialize embedding generator for provider {Provider}",
                providerConfig.Name);
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