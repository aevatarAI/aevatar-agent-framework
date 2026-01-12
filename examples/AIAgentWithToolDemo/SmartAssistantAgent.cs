using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging;

namespace AIAgentWithToolDemo;

/// <summary>
/// 带工具支持的智能助手
/// </summary>
public class SmartAssistantAgent : AIGAgentBase
{
    public SmartAssistantAgent() : base()
    {
        SystemPrompt = @"
你是一个智能助手，具有工具调用能力。

你可以使用以下工具:
1. calculator - 执行数学计算 (加减乘除)
2. get_weather - 查询城市天气信息

当用户需要计算时，使用 calculator 工具。
当用户询问天气时，使用 get_weather 工具。

请用简洁、友好的方式回答用户的问题。
";
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        Logger?.LogInformation("🔧 开始注册工具...");

        // 注册计算器工具
        var calculatorTool = new CalculatorTool();
        await RegisterToolAsync(calculatorTool, Logger, cancellationToken);
        Logger?.LogInformation("✅ 已注册工具: {Name} - {Description}", calculatorTool.Name, calculatorTool.Description);

        // 注册天气工具
        var weatherTool = new WeatherTool();
        await RegisterToolAsync(weatherTool, Logger, cancellationToken);
        Logger?.LogInformation("✅ 已注册工具: {Name} - {Description}", weatherTool.Name, weatherTool.Description);

        var tools = await GetRegisteredToolsAsync();
        var registeredCount = tools.Count;
        Logger?.LogInformation("🎉 工具注册完成！共 {Count} 个工具", registeredCount);

        if (registeredCount == 0)
        {
            Logger?.LogWarning("⚠️ 警告: GetRegisteredTools() 返回 0 个工具!");
        }
    }

    /// <summary>
    /// Get available tools for testing/debugging
    /// </summary>
    public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync()
    {
        return await GetRegisteredToolsAsync();
    }
}