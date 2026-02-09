using Aevatar.Agents.AI.Tool.Abstractions;

namespace Aevatar.Agents.AI.Tool.Evolution;

// ============================================================
//  Tool Evolution Registry (interface)
//
//  中文 + ASCII:
//  - 统一演化入口，保持执行路由与注册解耦。
// ============================================================
/// <summary>
/// Tool evolution registry (in-memory, best-effort).
/// </summary>
public interface IToolEvolutionRegistry
{
    void RegisterBaseTool(ToolDefinition tool);

    Task RegisterEvolvedToolAsync(
        ToolDefinition tool,
        ToolEvolutionPolicy? policy,
        CancellationToken cancellationToken = default);

    ToolDefinition? ResolveToolForExecution(string toolName);

    IReadOnlyList<ToolDefinition> GetActiveTools();
}
