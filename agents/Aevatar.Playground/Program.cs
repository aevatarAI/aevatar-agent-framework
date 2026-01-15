using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Playground;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddAevatarUserConfig()
    .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.Services.AddAevatarLocalRuntime().AddMEAI();

var app = builder.Build();

await app.StartAsync();

var factory = app.Services.GetRequiredService<IGAgentActorFactory>();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("=== Aevatar Chat Playground ===");

await new ChatAgentDemo().RunAsync(factory, logger);
//await new TranslationAgentDemo().RunAsync(factory, logger);

logger.LogInformation("Playground terminated.");

await app.StopAsync();