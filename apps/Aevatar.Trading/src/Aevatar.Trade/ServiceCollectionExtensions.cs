using Aevatar.Trade.Infrastructure.AiWars;
using Aevatar.Trade.Infrastructure.DecisionEngines;
using Aevatar.Trade.Infrastructure.Exchanges;
using Aevatar.Trade.Infrastructure.WeexApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Trade;

/// <summary>
/// Service registration extensions
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add trading system services (exchange-agnostic)
    /// </summary>
    public static IServiceCollection AddTradingExchangeServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ============ Configuration ============
        
        services.Configure<ExchangeConfig>(configuration.GetSection("Exchange"));
        services.Configure<WeexApiConfig>(configuration.GetSection("Weex"));
        services.Configure<TradingConfig>(configuration.GetSection("Policy:Trading"));
        services.Configure<AnalysisWeightConfig>(configuration.GetSection("Policy:Analysis"));
        services.Configure<RiskControlConfig>(configuration.GetSection("Policy:Risk"));
        services.Configure<TradeAuditConfig>(configuration.GetSection("TradeAudit"));
        services.Configure<MarketChatConfig>(configuration.GetSection("MarketChat"));
        services.Configure<TradingStartupConfig>(configuration.GetSection("Startup"));
        services.Configure<AiWarsLogUploadConfig>(configuration.GetSection("AiWars"));
        services.Configure<DecisionEngineConfig>(configuration.GetSection("DecisionEngine"));
        services.Configure<DecisionTriggerConfig>(configuration.GetSection("Policy:Trigger"));
        services.Configure<TradingPolicyConfig>(configuration.GetSection("Policy"));

        // ============ Exchange Credentials (Unified) ============
        var exchange = configuration.GetSection("Exchange").Get<ExchangeConfig>() ?? new ExchangeConfig();
        var credentials = ExchangeCredentialsResolver.BuildConfig(configuration);
        services.AddSingleton(credentials);
        services.PostConfigure<WeexApiConfig>(
            cfg => ExchangeCredentialsResolver.ApplyWeexCredentials(cfg, exchange, credentials));

        // ============ WEEX API ============
        
        var weex = configuration.GetSection("Weex").Get<WeexApiConfig>() ?? new WeexApiConfig();
        ExchangeCredentialsResolver.ApplyWeexCredentials(weex, exchange, credentials);
        if (weex.Mode == WeexApiMode.Spot)
        {
            services.AddHttpClient<IWeexApiClient, WeexSpotApiClient>((sp, client) =>
        {
                client.BaseAddress = new Uri(weex.BaseUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Aevatar.Trade/1.0");
            });
        }
        else
        {
            // 默认：AI Wars 合约（Contract）
            services.AddHttpClient<IWeexApiClient, WeexContractApiClient>((sp, client) =>
            {
                client.BaseAddress = new Uri(weex.BaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Aevatar.Trade/1.0");
        });
        }

        services.AddHttpClient<IWeexAiWarsLogClient, WeexAiWarsLogClient>((sp, client) =>
        {
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiWarsLogUploadConfig>>().Value;
            if (!string.IsNullOrWhiteSpace(cfg.BaseUrl))
            {
                client.BaseAddress = new Uri(cfg.BaseUrl);
            }
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddHttpClient<CognitiveMeshDecisionEngine>();

        services.AddSingleton<WeexWebSocketClient>();

        // ============ Exchange Adapter ============
        var exchangeType = exchange.ExchangeType == ExchangeType.Unspecified
            ? ExchangeType.Weex
            : exchange.ExchangeType;

        if (exchangeType == ExchangeType.Okx)
        {
            services.AddSingleton<IExchangeClient, OkxExchangeClient>();
        }
        else
        {
            services.AddSingleton<IExchangeClient, WeexExchangeClient>();
        }

        return services;
    }

    /// <summary>
    /// Compatibility shim for legacy entry point
    /// </summary>
    public static IServiceCollection AddWeexTradingServices(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddTradingExchangeServices(configuration);
}
