using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotNetFileSkillDemo;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // NOTE:
                // - dotnet run --project may keep CWD at repo root, so relative appsettings.json won't be found.
                // - We also load from AppContext.BaseDirectory (copied via csproj CopyToOutputDirectory).
                var baseDir = AppContext.BaseDirectory;

                config.AddJsonFile("appsettings.json", optional: true)
                    .AddAevatarUserSecrets()
                    .AddJsonFile("appsettings.secrets.json", optional: true)
                    .AddJsonFile(Path.Combine(baseDir, "appsettings.json"), optional: true, reloadOnChange: false)
                    .AddJsonFile(Path.Combine(baseDir, "appsettings.secrets.json"), optional: true, reloadOnChange: false);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;

                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // Configure LLM Providers (MEAI factory supports OpenAI/Azure OpenAI/Azure AI Inference)
                services.Configure<LLMProvidersConfig>(config.GetSection("LLMProviders"));
                services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();

                // Local runtime (creates agents via AIGAgentFactory + injectors)
                services.AddAevatarLocalRuntime();
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DotNetFileSkillDemo");
        var actorFactory = host.Services.GetRequiredService<IGAgentActorFactory>();

        logger.LogInformation("╔════════════════════════════════════════════╗");
        logger.LogInformation("║     DotNet File Skills Demo (LLM)          ║");
        logger.LogInformation("║   .NET 10: dotnet run --file tools         ║");
        logger.LogInformation("╚════════════════════════════════════════════╝");

        // Create agent via runtime so DI injectors work (LLMProviderFactory, etc.)
        var actor = await actorFactory.CreateGAgentActorAsync<FileSkillAgent>();
        var agent = (FileSkillAgent)actor.GetAgent();

        // 0) Smoke-test skills without any LLM (direct tool execution)
        logger.LogInformation("\n▶ Smoke test: execute skills directly (no LLM) ...");
        await RunToolAsync(logger, agent, "get_time", new Dictionary<string, object>());
        await RunToolAsync(logger, agent, "system_info", new Dictionary<string, object>());
        await RunToolAsync(logger, agent, "get_env", new Dictionary<string, object> { ["key"] = "SHELL" });

        // 1) Initialize LLM so the model can decide to call tools
        logger.LogInformation("\n▶ Initializing LLM provider '{Provider}' ...", AevatarAgentsConstants.DefaultProviderName);
        try
        {
            await agent.InitializeAsync(
                AevatarAgentsConstants.DefaultProviderName,
                config =>
                {
                    config.Model = "deepseek-chat";
                    config.Temperature = 0.2f;
                    config.MaxOutputTokens = 800;
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "LLM init failed. Add API key via ~/.aevatar/secrets.json (recommended) or examples/DotNetFileSkillDemo/appsettings.secrets.json then retry.");
            return;
        }

        logger.LogInformation("✅ LLM initialized.");

        // Show dotnet-file tools
        var tools = await agent.GetRegisteredToolsAsync();
        logger.LogInformation("\nRegistered dotnet-file skills:");
        foreach (var t in tools.Where(t => t.Tags.Contains("dotnet")))
        {
            logger.LogInformation("- {Name}: {Description}", t.Name, t.Description);
        }

        // 2) Scripted prompts (LLM should call tools)
        logger.LogInformation("\n▶ Scripted LLM prompts ...");
        await RunChatAsync(logger, agent, "请先调用 skills_list，然后 skills_load 加载 time-helper skill，再按 skill 的步骤回答：现在几点？");
        await RunChatAsync(logger, agent, "现在几点？请调用 get_time 工具获取真实时间。");
        await RunChatAsync(logger, agent, "请用 system_info 工具给我一个系统信息摘要（os/framework/pid）。");
        await RunChatAsync(logger, agent, "请调用 get_env 工具读取环境变量 SHELL。");

        // 3) Interactive loop
        logger.LogInformation("\n▶ Interactive chat (type 'exit' to quit) ...");
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\nUser> ");
            Console.ResetColor();

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            await RunChatAsync(logger, agent, input);
        }

        logger.LogInformation("\nDone.");
    }

    private static async Task RunToolAsync(
        ILogger logger,
        FileSkillAgent agent,
        string toolName,
        Dictionary<string, object> parameters)
    {
        var result = await agent.CallToolAsync(toolName, parameters);

        var title = $"[tool:{toolName}]";
        if (!result.IsSuccess)
        {
            logger.LogWarning("{Title} FAILED: {Error}", title, result.ErrorMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Content))
        {
            logger.LogInformation("{Title} (empty)", title);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Content);
            var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            logger.LogInformation("{Title}\n{Json}", title, pretty);
        }
        catch
        {
            logger.LogInformation("{Title} {Text}", title, result.Content);
        }
    }

    private static async Task RunChatAsync(
        ILogger logger,
        FileSkillAgent agent,
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest
        {
            Message = message,
            RequestId = Guid.NewGuid().ToString("N")
        };

        logger.LogInformation("\nUser: {Message}", message);
        var response = await agent.ChatAsync(request, cancellationToken);
        logger.LogInformation("Assistant: {Content}", response.Content);

        if (response.ToolCalled)
        {
            var args = response.ToolCall?.Arguments != null
                ? string.Join(", ", response.ToolCall.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))
                : string.Empty;

            logger.LogInformation("[ToolCalled] {Tool}({Args}) -> {Result}",
                response.ToolCall?.ToolName,
                args,
                response.ToolCall?.Result);
        }
    }
}

internal sealed class FileSkillAgent : AIGAgentBase
{
    public FileSkillAgent()
    {
        // Agent Skills
        EnableAgentSkills = true;
        AddAgentSkillsRoot(Path.Combine(AppContext.BaseDirectory, "agent_skills"));

        // Base prompt; AIGAgentBase will append tool list automatically.
        SystemPrompt =
            "You are a helpful AI assistant.\n" +
            "For any runtime-dependent question (time/system/env), you MUST call the relevant tool.\n" +
            "Prefer tools get_time, system_info, get_env for real data.";
    }

    public override Task<string> GetDescriptionAsync() => Task.FromResult("DotNetFileSkillAgent");

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        var skillsDir = Path.Combine(AppContext.BaseDirectory, "skills");
        await RegisterDotNetFileSkillAsync(Path.Combine(skillsDir, "get_time.cs"), cancellationToken);
        await RegisterDotNetFileSkillAsync(Path.Combine(skillsDir, "system_info.cs"), cancellationToken);
        await RegisterDotNetFileSkillAsync(Path.Combine(skillsDir, "get_env.cs"), cancellationToken);
    }

    /// <summary>
    /// Direct tool execution (no LLM) - handy for smoke tests.
    /// </summary>
    public async Task<Aevatar.Agents.AI.ToolExecutionResult> CallToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        // Ensure the tool system is initialized (built-ins + caches)
        await InitializeToolsAsync(cancellationToken);
        return await ToolManager.ExecuteToolAsync(toolName, parameters, context: null, cancellationToken);
    }
}

