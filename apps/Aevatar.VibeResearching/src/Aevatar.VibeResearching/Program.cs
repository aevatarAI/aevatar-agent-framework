using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VibeResearching;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                // Blogging
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // Configure LLM Providers
                // Ensure your appsettings.json has "LLMProviders" section
                services.Configure<LLMProvidersConfig>(context.Configuration.GetSection("LLMProviders"));
                services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();
                services.AddTransient<IAevatarToolManager, AevatarToolManager>();

                // Register Agent Factories
                // This registers IGAgentActorFactory and others
                services.AddAevatarLocalRuntime();
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var actorFactory = host.Services.GetRequiredService<IGAgentActorFactory>();

        logger.LogInformation("╔════════════════════════════════════════════╗");
        logger.LogInformation("║      Scientific Research Assistant         ║");
        logger.LogInformation("║    Powered by Claude Scientific Skills     ║");
        logger.LogInformation("╚════════════════════════════════════════════╝\n");

        try
        {
            logger.LogInformation("▶ Creating Research Agent...");
            var actor = await actorFactory.CreateGAgentActorAsync<ResearchAgent>();
            
            // In local runtime, we can cast to the actual agent instance
            // In distributed runtime (Orleans/Proto.Actor), this would be a proxy
            var agent = (ResearchAgent)actor.GetAgent();

            // Initialize AI
            // We use "default" as the provider name, expecting it in appsettings.json
            await agent.InitializeAsync(
                "default", 
                config =>
                {
                    config.Model = "gpt-4o"; // Or your preferred model
                });

            logger.LogInformation("✅ Agent initialized");

            var tools = await agent.GetRegisteredToolsAsync();
            logger.LogInformation("📚 Registered Tools: {Count}", tools.Count);

            // Interactive Loop
            Console.WriteLine("\nReady! Type your scientific query (or 'exit' to quit):");
            
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

                Console.WriteLine("Thinking...");
                var response = await agent.ChatAsync(request);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\nAssistant: {response.Content}");
                Console.ResetColor();

                if (response.ToolCalled)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Tool Called]: {response.ToolCall?.ToolName}");
                    Console.ResetColor();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Execution failed");
            if (ex.InnerException != null)
                logger.LogError(ex.InnerException, "Inner error");
        }
    }
}
