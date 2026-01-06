using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkillsMCPUnifiedDemo;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, config) =>
            {
                // dotnet run --project may keep CWD at repo root, so load both relative + output dir.
                var baseDir = AppContext.BaseDirectory;

                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false)
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

                // LLM providers
                services.Configure<LLMProvidersConfig>(config.GetSection("LLMProviders"));
                services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();

                // Local runtime (agent factory + injectors)
                services.AddAevatarLocalRuntime();
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SkillsMCPUnifiedDemo");
        var actorFactory = host.Services.GetRequiredService<IGAgentActorFactory>();

        // Provide a stable "workspace root" hint for dotnet-file tools (inherited by child processes).
        // Default to current working directory (repo root when run via `dotnet run --project ...`).
        var demoRoot = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("AEVATAR_DEMO_ROOT", demoRoot);
        logger.LogInformation("AEVATAR_DEMO_ROOT = {Root}", demoRoot);

        logger.LogInformation("╔════════════════════════════════════════════╗");
        logger.LogInformation("║       Skills + MCP Unified Demo            ║");
        logger.LogInformation("╚════════════════════════════════════════════╝");

        var actor = await actorFactory.CreateGAgentActorAsync<UnifiedAgent>();
        var agent = (UnifiedAgent)actor.GetAgent();

        // Init LLM
        logger.LogInformation("▶ Initializing LLM provider '{Provider}' ...", AevatarAgentsConstants.DefaultProviderName);
        try
        {
            await agent.InitializeAsync(
                AevatarAgentsConstants.DefaultProviderName,
                cfg =>
                {
                    cfg.Model = "deepseek-chat";
                    cfg.Temperature = 0.2f;
                    cfg.MaxOutputTokens = 900;
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "LLM init failed. Add API key to examples/SkillsMCPUnifiedDemo/appsettings.secrets.json then retry.");
            return;
        }

        logger.LogInformation("✅ LLM initialized.");

        // Scripted prompts (shows: skills_list/skills_load -> allowlist -> dotnet-file tool)
        // await RunChatAsync(logger, agent, "请先调用 skills_list，然后 skills_load 加载 time-helper skill，再按 skill 的步骤回答：现在几点？");
        // await RunChatAsync(logger, agent, "用 system_info 给我一个系统信息摘要（os/framework/pid）。");
        // await RunChatAsync(logger, agent, "请先调用 skills_list，然后 skills_load 加载 env-helper skill，再按 skill 的步骤回答：我的 SHELL 环境变量是什么？");
        // await RunChatAsync(logger, agent,
        //     "请先调用 skills_list，然后 skills_load 加载 file-searcher skill，再按 skill 的步骤回答：在仓库里搜索 'RegisterDotNetFileSkillAsync' 出现在哪些文件？");
        // await RunChatAsync(logger, agent,
        //     "请先调用 skills_list，然后 skills_load 加载 json-pretty skill，把这个 JSON 格式化后返回：{\"a\":1,\"b\":{\"c\":2,\"d\":[3,4]}}");
        // await RunChatAsync(logger, agent,
        //     "请先调用 skills_list，然后 skills_load 加载 slugify-helper skill，把 'Hello, Aevatar Agent Framework!' 转成 slug。");
        // await RunChatAsync(logger, agent,
        //     "请先调用 skills_list，然后 skills_load 加载 python-calc skill，再按 skill 的步骤回答：计算 1/7 的小数（至少 30 位）。");

        // Optional MCP demo: Context7 (if tools are present)
        var tools = await agent.GetRegisteredToolsAsync();
        var hasContext7 =
            tools.Any(t => string.Equals(t.Name, "resolve-library-id", StringComparison.OrdinalIgnoreCase)) &&
            tools.Any(t => string.Equals(t.Name, "get-library-docs", StringComparison.OrdinalIgnoreCase));

        if (hasContext7)
        {
            await RunChatAsync(logger, agent,
                "请先调用 skills_list，然后 skills_load 加载 context7-docs skill：查询 Orleans 文档里关于 streaming 的用法要点（给出要点列表）。");
        }
        else
        {
            logger.LogInformation("Context7 MCP tools not present, skipping Context7 scripted prompt.");
        }

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

    private static async Task RunChatAsync(
        ILogger logger,
        UnifiedAgent agent,
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = new Aevatar.Agents.AI.ChatRequest
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

