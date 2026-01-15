using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using AIAgentWithToolDemo;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// REFLECTION DEBUG START
Console.WriteLine("Inspecting AIFunctionFactory:");
foreach (var method in typeof(AIFunctionFactory).GetMethods(BindingFlags.Public | BindingFlags.Static))
{
    Console.WriteLine($"  {method.Name}({string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
}

Console.WriteLine("\nInspecting AIFunction:");
foreach (var ctor in typeof(AIFunction).GetConstructors())
{
    Console.WriteLine($"  ctor({string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
}

foreach (var prop in typeof(AIFunction).GetProperties())
{
        Console.WriteLine($"  Prop: {prop.PropertyType.Name} {prop.Name}");
}
// REFLECTION DEBUG END

// 1. 配置依赖注入
var services = new ServiceCollection();

// ============================================================================
// Build Host with Dependency Injection
// ============================================================================
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true)
            .AddAevatarUserConfig()
            .AddJsonFile("appsettings.secrets.json", optional: true);
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
logger.LogInformation("║   AI Agent with Tool Support Demo         ║");
logger.LogInformation("║   测试工具调用功能                          ║");
logger.LogInformation("╚════════════════════════════════════════════╝\n");




try
{
    // ========================================================================
    // 1. Create Smart Assistant Agent
    // ========================================================================
    logger.LogInformation("▶ 创建智能助手 Agent...");
    var actor = await actorFactory.CreateGAgentActorAsync<SmartAssistantAgent>();
    var agent = (SmartAssistantAgent) actor.GetAgent();
    
    // Initialize AI with configured LLM provider from appsettings
    await agent.InitializeAsync(
        "deepseek", // Use the provider configured in appsettings.json
        config =>
        {
            config.Model = "deepseek-chat";
            config.Temperature = 0.7f;
            config.MaxOutputTokens = 1000;
        });
    logger.LogInformation("✅ Agent 创建完成");
    
    // Verify tools are registered
    var tools = await agent.GetAvailableToolsAsync();
    logger.LogInformation("📋 已注册工具数量: {Count}", tools.Count);
    foreach (var tool in tools)
    {
        logger.LogInformation("  - {Name}: {Description}", tool.Name, tool.Description);
    }
    logger.LogInformation("");

    // ========================================================================
    // 3. Test 1: 数学计算
    // ========================================================================
    logger.LogInformation("╔════ 测试 1: 数学计算 ════╗");
    var calcRequest = new ChatRequest
    {
        Message = "帮我算一下 123 加 456 等于多少？",
        RequestId = Guid.NewGuid().ToString()
    };

    logger.LogInformation("👤 用户: {Message}", calcRequest.Message);
    var calcResponse = await agent.ChatAsync(calcRequest);
    logger.LogInformation("🤖 助手: {Response}\n", calcResponse.Content);

    if (calcResponse.ToolCalled)
    {
        logger.LogInformation("✅ 工具调用成功: {Tool}", calcResponse.ToolCall?.ToolName);
        logger.LogInformation("   参数: {Args}", 
            calcResponse.ToolCall?.Arguments != null 
                ? string.Join(", ", calcResponse.ToolCall.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))
                : "");
        logger.LogInformation("   结果: {Result}\n", calcResponse.ToolCall?.Result);
    }

    // ========================================================================
    // 4. Test 2: 天气查询
    // ========================================================================
    logger.LogInformation("╔════ 测试 2: 天气查询 ════╗");
    var weatherRequest = new ChatRequest
    {
        Message = "北京今天天气怎么样？",
        RequestId = Guid.NewGuid().ToString()
    };

    logger.LogInformation("👤 用户: {Message}", weatherRequest.Message);
    var weatherResponse = await agent.ChatAsync(weatherRequest);
    logger.LogInformation("🤖 助手: {Response}\n", weatherResponse.Content);

    if (weatherResponse.ToolCalled)
    {
        logger.LogInformation("✅ 工具调用成功: {Tool}", weatherResponse.ToolCall?.ToolName);
        logger.LogInformation("   参数: {Args}", 
            weatherResponse.ToolCall?.Arguments != null 
                ? string.Join(", ", weatherResponse.ToolCall.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))
                : "");
        logger.LogInformation("   结果: {Result}\n", weatherResponse.ToolCall?.Result);
    }

    // ========================================================================
    // 5. Test 3: 复杂计算
    // ========================================================================
    logger.LogInformation("╔════ 测试 3: 复杂计算 ════╗");
    var complexCalcRequest = new ChatRequest
    {
        Message = "50 乘以 8 是多少？然后除以 4",
        RequestId = Guid.NewGuid().ToString()
    };

    logger.LogInformation("👤 用户: {Message}", complexCalcRequest.Message);
    var complexCalcResponse = await agent.ChatAsync(complexCalcRequest);
    logger.LogInformation("🤖 助手: {Response}\n", complexCalcResponse.Content);

    // ========================================================================
    // 6. Test 4: 普通对话（不使用工具）
    // ========================================================================
    logger.LogInformation("╔════ 测试 4: 普通对话 ════╗");
    var chatRequest = new ChatRequest
    {
        Message = "你好，请介绍一下你自己",
        RequestId = Guid.NewGuid().ToString()
    };

    logger.LogInformation("👤 用户: {Message}", chatRequest.Message);
    var chatResponse = await agent.ChatAsync(chatRequest);
    logger.LogInformation("🤖 助手: {Response}\n", chatResponse.Content);

    // ========================================================================
    // 7. Display History
    // ========================================================================
    logger.LogInformation("╔════ 对话历史 ════╗");
    var state = agent.GetState();
    logger.LogInformation("📊 总消息数: {Count}", state.History.Count);
    foreach (var msg in state.History)
    {
        var role = msg.Role.ToString();
        var preview = msg.Content?.Length > 50 
            ? msg.Content.Substring(0, 50) + "..." 
            : msg.Content ?? "[工具调用]";
        logger.LogInformation("  {Role}: {Preview}", role, preview);
        // logger.LogInformation("  [DEBUG] Full Message: {Msg}", msg.ToString());
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "❌ Demo 执行出错");
}

logger.LogInformation("\n╔════════════════════════════════════════════╗");
logger.LogInformation("║           Demo 完成! 🎉                    ║");
logger.LogInformation("╚════════════════════════════════════════════╝");
