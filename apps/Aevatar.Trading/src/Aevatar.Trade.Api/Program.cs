using Aevatar.Agents.Core.Extensions;
using Aevatar.Trade;
using Aevatar.Trade.AgUi;
using Aevatar.Trade.Api;
using Aevatar.Trade.Api.AgUi;
using Aevatar.Trade.Api.Extensions;
using Aevatar.Trade.Infrastructure.Exchanges;
using Aevatar.Trade.Infrastructure.WeexApi;
using Orleans.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ============ Secrets ============
// Global user-level secrets (encrypted, per-user) - best-effort.
// - Default: ~/.aevatar/secrets.json
// - Override: AEVATAR_SECRETS_PATH / AEVATAR_SECRETS_DIR
builder.Configuration.AddAevatarUserConfig();

// Load local secrets file (gitignored) for quick hackathon setup.
// NOTE: Environment variables can still be used, but this file enables "drop-in" setup on a new machine.
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);

// ============ Aspire + Observability ============

builder.AddServiceDefaults();
builder.AddAevatarObservability();

// ============ Export WEEX credentials to env (for dotnet file skills) ============
// DotNetFileSkillTool executes a separate process (dotnet run --file) which reads WEEX_* from env.
// Only bridge env when ExchangeType is WEEX (or unspecified for backward compatibility).
var exchangeConfig = builder.Configuration.GetSection("Exchange").Get<ExchangeConfig>();
var exchangeCredentials = ExchangeCredentialsResolver.BuildConfig(builder.Configuration);
var exchangeType = exchangeConfig?.ExchangeType ?? ExchangeType.Unspecified;
var useWeex = exchangeType == ExchangeType.Unspecified || exchangeType == ExchangeType.Weex;
if (useWeex)
{
    var weexEnv = builder.Configuration.GetSection("Weex").Get<WeexApiConfig>() ?? new WeexApiConfig();
    ExchangeCredentialsResolver.ApplyWeexCredentials(weexEnv, exchangeConfig ?? new ExchangeConfig(), exchangeCredentials);
    if (weexEnv != null)
    {
        if (!string.IsNullOrWhiteSpace(weexEnv.BaseUrl))
            Environment.SetEnvironmentVariable("WEEX_BASE_URL", weexEnv.BaseUrl);
        if (!string.IsNullOrWhiteSpace(weexEnv.ApiKey))
            Environment.SetEnvironmentVariable("WEEX_API_KEY", weexEnv.ApiKey);
        if (!string.IsNullOrWhiteSpace(weexEnv.ApiSecret))
            Environment.SetEnvironmentVariable("WEEX_API_SECRET", weexEnv.ApiSecret);
        if (!string.IsNullOrWhiteSpace(weexEnv.Passphrase))
            Environment.SetEnvironmentVariable("WEEX_PASSPHRASE", weexEnv.Passphrase);
    }
}

// ============ Runtime Configuration ============

var runtimeOptions = builder.Configuration
    .GetSection(AgentRuntimeOptions.SectionName)
    .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

// Orleans configuration
if (runtimeOptions.RuntimeType == AgentRuntimeType.Orleans)
{
    builder.Host.UseOrleans((context, siloBuilder) =>
    {
        var orleansOptions = runtimeOptions.Orleans;

        if (orleansOptions.UseLocalhostClustering)
        {
            siloBuilder.UseLocalhostClustering(
                siloPort: orleansOptions.SiloPort,
                gatewayPort: orleansOptions.GatewayPort);
        }
        else
        {
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = orleansOptions.ClusterId;
                options.ServiceId = orleansOptions.ServiceId;
            });
        }

        siloBuilder.AddMemoryGrainStorage("AgentStore");

        Console.WriteLine($"🌐 Orleans Silo configuration completed");
        Console.WriteLine($"   ClusterId: {orleansOptions.ClusterId}");
        Console.WriteLine($"   ServiceId: {orleansOptions.ServiceId}");
    });
}

// ============ Services ============

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Agent runtime
builder.Services.AddAgentRuntime(builder.Configuration);
builder.Services.AddGAgentActorFactoryProvider();

// Trading services (exchange-agnostic)
builder.Services.AddTradingExchangeServices(builder.Configuration);

// LLM Provider (using MEAI)
builder.Services.AddMEAILLMProvider(builder.Configuration);

// Trading system
builder.Services.AddSingleton<TradingSystem>();

// ------------------------------------------------------------
// AG-UI Streaming（snapshot + SSE）
// ------------------------------------------------------------
builder.Services.AddSingleton<TradeAgUiHub>();
builder.Services.AddSingleton<ITradeAgUiStreamSink, TradeAgUiStreamSink>();
builder.Services.AddHostedService<TradeAgUiAuditBridge>();
builder.Services.AddHostedService<TradingSystemAutoStarter>();

// ============ Build & Configure ============

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // ---------------------------------------------------------------------
    // OpenAPI (built-in, no Swashbuckle)
    // ---------------------------------------------------------------------
    // Keep the historical path shape so existing tooling/docs keep working:
    //   - OpenAPI JSON: /swagger/v1/swagger.json
    app.MapOpenApi("/swagger/{documentName}/swagger.json");

    // Lightweight Swagger UI (loads JS/CSS from CDN; no large static assets in repo)
    //   - UI: /swagger
    app.MapGet("/swagger", () => Results.Text("""
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>WEEX AI Trading API - Swagger UI</title>
    <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css" />
    <style>
      html, body { height: 100%; margin: 0; }
      #swagger-ui { height: 100%; }
    </style>
  </head>
  <body>
    <div id="swagger-ui"></div>
    <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
    <script>
      window.onload = () => {
        SwaggerUIBundle({
          url: '/swagger/v1/swagger.json',
          dom_id: '#swagger-ui',
          deepLinking: true,
          presets: [
            SwaggerUIBundle.presets.apis,
            SwaggerUIBundle.SwaggerUIStandalonePreset
          ],
          layout: 'BaseLayout'
        });
      };
    </script>
  </body>
</html>
""", "text/html"));
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.UsePrometheusMetrics();

app.MapControllers();
app.MapTradeAgUiEvents();

// AI Wars DotNetSkills -> HTTP endpoints (Swagger visible)
await app.MapAiWarsSkillEndpointsAsync();
app.MapDefaultEndpoints();

// ============ Startup Banner ============

Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║        🤖 WEEX AI Trading System - Multi-Agent Trading        ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║  Runtime: {runtimeOptions.RuntimeType,-20}                         ║");
Console.WriteLine("║                                                               ║");
Console.WriteLine("║  Endpoints:                                                   ║");
Console.WriteLine("║    📊 Swagger:  http://localhost:7100/swagger                 ║");
Console.WriteLine("║    📈 Metrics:  http://localhost:7100/metrics                 ║");
Console.WriteLine("║    💓 Health:   http://localhost:7100/health                  ║");
Console.WriteLine("║                                                               ║");
Console.WriteLine("║  Agents:                                                      ║");
Console.WriteLine("║    🔌 DataCollector    - Data Collection                      ║");
Console.WriteLine("║    😱 SentimentAgent   - Sentiment Analysis (AI)             ║");
Console.WriteLine("║    📊 TechnicalAgent   - Technical Analysis (AI)            ║");
Console.WriteLine("║    🎯 Coordinator      - Decision Coordination (AI)          ║");
Console.WriteLine("║    🛡️ RiskManager      - Risk Management (AI)                 ║");
Console.WriteLine("║    ⚡ Executor         - Trade Execution                     ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

app.Run();
