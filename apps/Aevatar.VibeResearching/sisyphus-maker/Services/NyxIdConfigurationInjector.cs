namespace SisyphusMaker.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SisyphusMaker.Dtos;

/// <summary>
/// Per-request service that injects a dynamic LLM provider into <see cref="IConfiguration"/>
/// when a NyxID delegation token is present. The injected provider routes LLM calls through
/// the NyxID LLM Gateway using the delegation token as Bearer auth.
/// Cleans up injected config keys on disposal.
/// </summary>
public sealed class NyxIdConfigurationInjector : IDisposable
{
    private const string ProviderPrefix = "LLMProviders:Providers:";

    private readonly IConfiguration _configuration;
    private readonly NyxGatewayOptions _options;
    private readonly ILogger<NyxIdConfigurationInjector> _logger;

    private string? _providerName;
    private bool _disposed;

    public NyxIdConfigurationInjector(
        IConfiguration configuration,
        IOptions<NyxGatewayOptions> options,
        ILogger<NyxIdConfigurationInjector> logger)
    {
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>The unique provider name injected into config, or null if not a NyxID request.</summary>
    public string? ResolvedProviderName => _providerName;

    /// <summary>Whether this request was identified as a NyxID-proxied request.</summary>
    public bool IsNyxIdRequest => _providerName is not null;

    /// <summary>Extracted NyxID identity information from request headers.</summary>
    public NyxIdIdentity? Identity { get; private set; }

    /// <summary>
    /// Attempts to extract a NyxID delegation token from request headers and inject
    /// a corresponding LLM provider config into <see cref="IConfiguration"/>.
    /// </summary>
    /// <param name="headers">The HTTP request headers.</param>
    /// <param name="modelOverride">Optional model override from the request body.</param>
    /// <returns>True if a gateway provider was injected; false for fallback behavior.</returns>
    public bool TryInjectFromHeaders(IHeaderDictionary headers, string? modelOverride)
    {
        var token = headers[NyxIdHeaders.DelegationToken].ToString();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (!_options.Enabled)
        {
            _logger.LogDebug("NyxID delegation token present but gateway is disabled");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _logger.LogWarning(
                "NyxID delegation token present but NyxGateway:BaseUrl is not configured — falling back to direct provider");
            return false;
        }

        // Extract identity headers for audit logging
        Identity = new NyxIdIdentity(
            UserId: headers[NyxIdHeaders.UserId].ToString(),
            UserEmail: headers[NyxIdHeaders.UserEmail].ToString(),
            UserName: headers[NyxIdHeaders.UserName].ToString());

        // Generate unique provider name to avoid cross-request contamination
        _providerName = $"nyx-gateway-{Guid.NewGuid().ToString("N")[..8]}";
        var model = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride : _options.DefaultModel;
        var prefix = $"{ProviderPrefix}{_providerName}:";

        _configuration[$"{prefix}ProviderType"] = _options.ProviderType;
        _configuration[$"{prefix}Endpoint"] = _options.GetGatewayEndpoint();
        _configuration[$"{prefix}ApiKey"] = token;
        _configuration[$"{prefix}Model"] = model;
        _configuration[$"{prefix}EnableStreaming"] = "true";

        _logger.LogInformation(
            "NyxID gateway provider injected: {ProviderName}, model={Model}, user={UserId}",
            _providerName, model, Identity.UserId);

        return true;
    }

    /// <summary>Cleans up injected configuration keys to prevent stale entries.</summary>
    public void Dispose()
    {
        if (_disposed || _providerName is null)
            return;
        _disposed = true;

        var prefix = $"{ProviderPrefix}{_providerName}:";
        _configuration[$"{prefix}ProviderType"] = null;
        _configuration[$"{prefix}Endpoint"] = null;
        _configuration[$"{prefix}ApiKey"] = null;
        _configuration[$"{prefix}Model"] = null;
        _configuration[$"{prefix}EnableStreaming"] = null;

        _logger.LogDebug("NyxID gateway provider cleaned up: {ProviderName}", _providerName);
    }
}

/// <summary>
/// Identity information extracted from NyxID-injected headers.
/// </summary>
public sealed record NyxIdIdentity(string UserId, string UserEmail, string UserName);
