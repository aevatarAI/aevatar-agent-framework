using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Learning.Context;
using Aevatar.Learning.Notebooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Learning.Chat;

// ============================================================
//  LearningChatService (MVP)
//
//  Goals:
//  - Build bounded notebook context via LearningContextBuilder
//  - Select provider via LLMProviders (default + request override)
//  - Expose streaming tokens (for AG-UI TEXT_MESSAGE_* pipeline)
//
//  NOTE:
//  - Tool calling / citations are added in later tasks.
// ============================================================
public sealed class LearningChatService
{
    private readonly LearningContextBuilder _contextBuilder;
    private readonly ILLMProviderFactory _providers;
    private readonly IOptions<LLMProvidersConfig> _config;
    private readonly ILogger<LearningChatService> _logger;

    public LearningChatService(
        LearningContextBuilder contextBuilder,
        ILLMProviderFactory providers,
        IOptions<LLMProvidersConfig> config,
        ILogger<LearningChatService> logger)
    {
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LearningChatRun> StartAsync(LearningChatInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Workspace);

        var message = (input.Message ?? string.Empty).Trim();
        if (message.Length == 0)
            throw new ArgumentException("message is required.", nameof(input));

        // 1) Build bounded context (deterministic + traceable)
        var ctx = await _contextBuilder.BuildAsync(
            input.Workspace,
            query: message,
            selectedSourceIds: input.SelectedSourceIds,
            budget: input.Budget,
            ct: ct);

        // 2) Choose provider
        var providerName = ResolveProviderName(input.ProviderName);
        var provider = _providers.GetProvider(providerName);

        // 3) Build request
        var system = BuildSystemPrompt(ctx.Text);
        var req = new AevatarLLMRequest
        {
            SystemPrompt = system,
            UserPrompt = message,
            Messages = input.History == null
                ? new List<AevatarChatMessage>()
                : input.History.Take(24).ToList(),
            Settings = new AevatarLLMSettings()
        };

        _logger.LogInformation(
            "[LearningChat] Start: provider={Provider}, notebook={NotebookId}, sources={Sources}, budget(total={Total}, per={Per}, maxSources={MaxSources})",
            providerName,
            input.Workspace.NotebookId,
            ctx.Slices.Count,
            ctx.Budget.MaxTotalChars,
            ctx.Budget.MaxPerSourceChars,
            ctx.Budget.MaxSources);

        // 4) Streaming tokens (AG-UI layer will map to TEXT_MESSAGE_CONTENT)
        var tokens = provider.GenerateStreamAsync(req, ct);

        return new LearningChatRun(
            ProviderName: providerName,
            Context: ctx,
            Tokens: tokens);
    }

    private string ResolveProviderName(string? overrideName)
    {
        var name = (overrideName ?? string.Empty).Trim();
        if (name.Length > 0)
            return name;

        name = (_config.Value.Default ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("LLMProviders:Default is not configured.");

        return name;
    }

    private static string BuildSystemPrompt(string contextText)
    {
        // Keep prompt short and deterministic.
        var basePrompt =
            """
            You are an assistant for an AI-native learning notebook.

            Rules:
            - You MUST ground answers in the provided "Notebook context" when present.
            - If context is insufficient, say what is missing and ask a focused question.
            - Be concise and structured. Avoid hallucination.
            """;

        contextText = (contextText ?? string.Empty).Trim();
        if (contextText.Length == 0)
            return basePrompt;

        return $"{basePrompt}\n\nNotebook context:\n{contextText}\n";
    }
}

public sealed record LearningChatInput(
    NotebookWorkspace Workspace,
    string Message,
    string? ProviderName = null,
    IReadOnlyList<string>? SelectedSourceIds = null,
    LearningContextBudget? Budget = null,
    IReadOnlyList<AevatarChatMessage>? History = null);

public sealed record LearningChatRun(
    string ProviderName,
    LearningContextResult Context,
    IAsyncEnumerable<AevatarLLMToken> Tokens);


