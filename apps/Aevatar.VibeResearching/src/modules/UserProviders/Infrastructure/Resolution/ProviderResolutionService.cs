using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Aevatar.VibeResearching.UserProviders.Services;
using Aevatar.VibeResearching.UserProviders.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Resolution;

/// <summary>
/// Implements the 3-layer provider resolution chain:
///   Layer 1: Session agent mapping
///   Layer 2: User default provider
///   Layer 3: Platform default provider
/// </summary>
public sealed class ProviderResolutionService : IProviderResolutionService
{
    private readonly IUserLlmProviderRepository _providerRepo;
    private readonly IUserCodexTokenRepository _tokenRepo;
    private readonly IAgentProvidersRepository _agentProvidersRepo;
    private readonly IUserProviderEncryptionService _encryption;
    private readonly ILLMProviderFactory _llmProviderFactory;
    private readonly ICodexOAuthService _codexService;
    private readonly CodexOAuthOptions _codexOptions;
    private readonly ILogger<ProviderResolutionService> _logger;

    public ProviderResolutionService(
        IUserLlmProviderRepository providerRepo,
        IUserCodexTokenRepository tokenRepo,
        IAgentProvidersRepository agentProvidersRepo,
        IUserProviderEncryptionService encryption,
        ILLMProviderFactory llmProviderFactory,
        ICodexOAuthService codexService,
        IOptions<CodexOAuthOptions> codexOptions,
        ILogger<ProviderResolutionService> logger)
    {
        _providerRepo = providerRepo;
        _tokenRepo = tokenRepo;
        _agentProvidersRepo = agentProvidersRepo;
        _encryption = encryption;
        _llmProviderFactory = llmProviderFactory;
        _codexService = codexService;
        _codexOptions = codexOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResolvedProviderResult> ResolveAsync(
        string sessionId, string agentName, Guid userId, CancellationToken ct = default)
    {
        // Layer 1: Session agent mapping
        var snapshot = await _agentProvidersRepo.LoadAsync(sessionId, ct);
        if (snapshot?.Map != null &&
            snapshot.Map.TryGetValue(agentName, out var mappedNamespace) &&
            !string.IsNullOrWhiteSpace(mappedNamespace))
        {
            var ns = ProviderNamespace.Parse(mappedNamespace);
            var result = await ResolveNamespaceAsync(ns, userId, "session-agent-mapping", ct);
            if (result != null)
                return result;

            _logger.LogWarning(
                "Session agent mapping for '{Agent}' references '{Namespace}' which could not be resolved. Falling through.",
                agentName, mappedNamespace);
        }

        // Layer 2: User default provider
        var defaultProvider = await _providerRepo.GetDefaultByUserAsync(userId, ct);
        if (defaultProvider != null)
        {
            var result = await BuildResultFromUserProviderAsync(defaultProvider, userId, "user-default", ct);
            if (result != null)
                return result;
        }

        // Layer 3: Platform default provider
        try
        {
            var platformConfig = _llmProviderFactory.GetDefaultProviderConfig();
            var platformNs = ProviderNamespace.ForPlatform(platformConfig.Name).ToString();
            return new ResolvedProviderResult(
                platformNs,
                platformConfig.ProviderType,
                platformConfig.Model,
                platformConfig.Endpoint,
                "platform-default",
                platformConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No provider available at any resolution layer for agent '{Agent}'.", agentName);
            throw new InvalidOperationException(
                $"No provider available for agent '{agentName}'. Please configure a provider.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableProvider>> GetAvailableProvidersAsync(
        Guid userId, CancellationToken ct = default)
    {
        var result = new List<AvailableProvider>();

        // User providers
        var userProviders = await _providerRepo.GetByUserAsync(userId, ct);
        foreach (var p in userProviders)
        {
            var source = p.IsCodexOAuth ? "codex" : "user";
            result.Add(new AvailableProvider(
                Namespace: ProviderNamespace.ForUser(p.Id).ToString(),
                Name: p.Name,
                ProviderType: p.ProviderType,
                DefaultModel: p.DefaultModel,
                Source: source,
                IsDefault: p.IsDefault));
        }

        // Platform providers
        try
        {
            var platformNames = _llmProviderFactory.GetAvailableProviderNames();
            foreach (var name in platformNames)
            {
                try
                {
                    var config = _llmProviderFactory.GetProviderConfig(name);
                    result.Add(new AvailableProvider(
                        Namespace: ProviderNamespace.ForPlatform(name).ToString(),
                        Name: name,
                        ProviderType: config.ProviderType,
                        DefaultModel: config.Model,
                        Source: "platform",
                        IsDefault: false));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load platform provider config for '{Name}'.", name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list platform providers.");
        }

        return result;
    }

    private async Task<ResolvedProviderResult?> ResolveNamespaceAsync(
        ProviderNamespace ns, Guid userId, string source, CancellationToken ct)
    {
        if (ns.IsUser)
        {
            var provider = await _providerRepo.GetByIdAndUserAsync(ns.Identifier, userId, ct);
            if (provider == null)
                return null;

            return await BuildResultFromUserProviderAsync(provider, userId, source, ct);
        }

        // Platform
        if (_llmProviderFactory.HasProvider(ns.Identifier))
        {
            var config = _llmProviderFactory.GetProviderConfig(ns.Identifier);
            return new ResolvedProviderResult(
                ns.ToString(),
                config.ProviderType,
                config.Model,
                config.Endpoint,
                source,
                config);
        }

        return null;
    }

    private async Task<ResolvedProviderResult?> BuildResultFromUserProviderAsync(
        UserLlmProvider provider, Guid userId, string source, CancellationToken ct)
    {
        LLMProviderConfig config;

        if (provider.IsCodexOAuth)
        {
            try
            {
                var accessToken = await _codexService.GetValidAccessTokenAsync(userId, ct);
                var codexToken = await _tokenRepo.GetByUserAsync(userId, ct);
                config = BuildCodexConfig(provider, accessToken, codexToken?.ChatGptAccountId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get Codex access token for user {UserId}.", userId);
                return null;
            }
        }
        else
        {
            var apiKey = _encryption.Decrypt(provider.EncryptedApiKey);
            config = BuildUserProviderConfig(provider, apiKey);
        }

        var ns = ProviderNamespace.ForUser(provider.Id).ToString();
        return new ResolvedProviderResult(
            ns,
            provider.ProviderType,
            provider.DefaultModel,
            provider.Endpoint,
            source,
            config);
    }

    private static LLMProviderConfig BuildUserProviderConfig(UserLlmProvider provider, string decryptedApiKey)
    {
        return new LLMProviderConfig
        {
            Name = $"user:{provider.Id}",
            ProviderType = provider.ProviderType,
            ApiKey = decryptedApiKey,
            Model = provider.DefaultModel,
            Endpoint = provider.Endpoint,
            DeploymentName = provider.DeploymentName,
            Temperature = AevatarAIDefaults.DefaultTemperature,
            MaxTokens = AevatarAIDefaults.DefaultMaxOutputTokens,
            TimeoutMilliseconds = 600_000
        };
    }

    private LLMProviderConfig BuildCodexConfig(
        UserLlmProvider provider, string accessToken, string? accountId)
    {
        var settings = new Dictionary<string, object>
        {
            ["openai-beta"] = "codex-v1",
            ["originator"] = "aevatar"
        };

        if (!string.IsNullOrEmpty(accountId))
        {
            settings["chatgpt-account-id"] = accountId;
        }

        return new LLMProviderConfig
        {
            Name = $"user:{provider.Id}",
            ProviderType = "OpenAI",
            ApiKey = accessToken,
            Model = provider.DefaultModel,
            Endpoint = _codexOptions.CodexApiBaseUrl,
            Temperature = AevatarAIDefaults.DefaultTemperature,
            MaxTokens = AevatarAIDefaults.DefaultMaxOutputTokens,
            TimeoutMilliseconds = 600_000,
            ProviderSpecificSettings = settings
        };
    }
}
