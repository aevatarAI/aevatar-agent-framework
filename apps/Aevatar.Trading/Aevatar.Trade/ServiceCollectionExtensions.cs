using Aevatar.Trade.Infrastructure.WeexApi;
using Aevatar.Trade.Infrastructure.AiWars;
using Aevatar.Trade.Infrastructure.DecisionEngines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Trade;

/// <summary>
/// Service registration extensions
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add WEEX trading system services
    /// </summary>
    public static IServiceCollection AddWeexTradingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ============ Configuration ============
        
        services.Configure<WeexApiConfig>(configuration.GetSection("Weex"));
        services.Configure<TradingConfig>(configuration.GetSection("Trading"));
        services.Configure<AnalysisWeightConfig>(configuration.GetSection("Analysis"));
        services.Configure<RiskControlConfig>(configuration.GetSection("RiskControl"));
        services.Configure<TradeAuditConfig>(configuration.GetSection("TradeAudit"));
        services.Configure<AiWarsLogUploadConfig>(configuration.GetSection("AiWars"));
        services.Configure<DecisionEngineConfig>(configuration.GetSection("DecisionEngine"));

        // ============ WEEX API ============
        
        var weex = configuration.GetSection("Weex").Get<WeexApiConfig>() ?? new WeexApiConfig();
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

        return services;
    }
}
