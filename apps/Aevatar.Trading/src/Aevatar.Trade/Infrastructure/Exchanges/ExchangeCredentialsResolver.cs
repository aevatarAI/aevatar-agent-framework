using Aevatar.Trade.Infrastructure.WeexApi;
using Microsoft.Extensions.Configuration;

namespace Aevatar.Trade.Infrastructure.Exchanges;

// ============================================================================
//  ExchangeCredentialsResolver
//  - 统一解析 ExchangeCredentials 并应用到各交易所配置
// ============================================================================
public static class ExchangeCredentialsResolver
{
    public static ExchangeCredentialsConfig BuildConfig(IConfiguration configuration)
    {
        var result = new ExchangeCredentialsConfig();
        var section = configuration.GetSection("ExchangeCredentials");
        foreach (var child in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Key))
                continue;

            var entry = new ExchangeCredentialConfig();
            child.Bind(entry);
            result.Exchanges[child.Key.Trim()] = entry;
        }

        return result;
    }

    public static void ApplyWeexCredentials(
        WeexApiConfig config,
        ExchangeConfig exchangeConfig,
        ExchangeCredentialsConfig credentials)
    {
        var key = ResolveCredentialsKey(exchangeConfig);
        var entry = FindCredential(credentials, key);
        if (entry == null)
            return;

        if (!string.IsNullOrWhiteSpace(entry.Endpoint))
            config.BaseUrl = entry.Endpoint;
        if (!string.IsNullOrWhiteSpace(entry.ApiKey))
            config.ApiKey = entry.ApiKey;
        if (!string.IsNullOrWhiteSpace(entry.ApiSecret))
            config.ApiSecret = entry.ApiSecret;
        if (!string.IsNullOrWhiteSpace(entry.Passphrase))
            config.Passphrase = entry.Passphrase;
    }

    private static ExchangeCredentialConfig? FindCredential(
        ExchangeCredentialsConfig credentials,
        string key)
    {
        if (credentials.Exchanges.TryGetValue(key, out var entry))
            return entry;

        foreach (var (k, v) in credentials.Exchanges)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v;
        }

        return null;
    }

    private static string ResolveCredentialsKey(ExchangeConfig exchangeConfig)
    {
        if (!string.IsNullOrWhiteSpace(exchangeConfig.CredentialsKey))
            return exchangeConfig.CredentialsKey.Trim();

        var type = exchangeConfig.ExchangeType == ExchangeType.Unspecified
            ? ExchangeType.Weex
            : exchangeConfig.ExchangeType;
        return type.ToString().ToLowerInvariant();
    }
}
