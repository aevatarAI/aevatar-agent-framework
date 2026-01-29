using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.ToolPacks;

// ============================================================
//  IAevatarToolPack
//
//  说明：
//  - Tool pack 是可插拔工具集合（如 Aevatar.Agents.AI.Tools）。
//  - 通过 YAML tools allowlist 决定「是否注册」某个工具。
//  - 运行时使用 DI 注入 pack（避免 Core 反向依赖具体工具库）。
// ============================================================
public interface IAevatarToolPack
{
    string PackName { get; }
    IReadOnlyCollection<string> ToolNames { get; }

    bool TryCreateTool(string toolName, AevatarToolPackContext context, out IAevatarTool tool);
}

public sealed record AevatarToolPackContext(
    IConfiguration? Configuration,
    string? WorkingDirectory,
    ILogger? Logger);
