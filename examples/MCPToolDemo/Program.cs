using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using MCPToolDemo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ============================================================================
// Build Host with Dependency Injection
// ============================================================================
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true)
              .AddAevatarUserConfig()
              .AddJsonFile("appsettings.secrets.json", optional: true); // Load secrets
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;
        
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Configure LLM Providers
        services.Configure<LLMProvidersConfig>(config.GetSection("LLMProviders"));
        services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();
        services.AddTransient<IAevatarToolManager, AevatarToolManager>();

        // Register Agent Factories
        services.AddAevatarLocalRuntime();
    })
    .Build();

// ============================================================================
// Main Demo Execution
// ============================================================================
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var actorFactory = host.Services.GetRequiredService<IGAgentActorFactory>();

logger.LogInformation("╔════════════════════════════════════════════╗");
logger.LogInformation("║        MCP Tool Integration Demo           ║");
logger.LogInformation("║   Testing MCP Server (GitHub/Filesystem)   ║");
logger.LogInformation("╚════════════════════════════════════════════╝\n");

try
{
    // 1. Create MCP Agent
    logger.LogInformation("▶ Creating MCP Agent...");
    var actor = await actorFactory.CreateGAgentActorAsync<MCPAgent>();
    var agent = (MCPAgent) actor.GetAgent();
    
    // Initialize AI with configured LLM provider
    // Note: Ensure provider is configured via ~/.aevatar/secrets.json (recommended) or appsettings.secrets.json
    await agent.InitializeAsync(
        AevatarAgentsConstants.DefaultProviderName, 
        config =>
        {
            config.Model = "deepseek-chat";
            config.Temperature = 0.7f;
            config.MaxOutputTokens = 1000;
        });
    logger.LogInformation("✅ Agent initialized");
    
    // Verify tools
    var tools = await agent.GetRegisteredToolsAsync();
    logger.LogInformation("📋 Registered Tools: {Count}", tools.Count);
    foreach (var tool in tools)
    {
        logger.LogInformation("  - {Name}: {Description}", tool.Name, tool.Description);
    }
    logger.LogInformation("");

    // 2. Interactive Chat Loop
    logger.LogInformation("💬 Starting chat session (Type 'exit' to quit)");
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("\nUser: ");
        Console.ResetColor();
        
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "exit")
            break;

        var request = new ChatRequest
        {
            Message = input,
            RequestId = Guid.NewGuid().ToString()
        };

        var response = await agent.ChatAsync(request);
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\nAssistant: {response.Content}");
        Console.ResetColor();

        if (response.ToolCalled)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Tool Called]: {response.ToolCall?.ToolName}");
            Console.WriteLine($"[Arguments]: {string.Join(", ", response.ToolCall?.Arguments.Select(kv => $"{kv.Key}={kv.Value}") ?? Array.Empty<string>())}");
            Console.WriteLine($"[Result]: {response.ToolCall?.Result}");
            Console.ResetColor();
        }
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "❌ Demo execution failed");
}

logger.LogInformation("\nDemo finished.");
