using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tools;

namespace Aevatar.Platform.Core.Workflow;

// ============================================================
//  HermesAIGAgent
//
//  说明：
//  - Hermes 专用 Agent：保持 Role 行为 + 增加文件读写工具
//  - 具体文件权限由 FileToolOptions 控制（路径白名单）
// ============================================================
public sealed class HermesAIGAgent : AIGAgentBase
{
    private FileToolOptions _fileToolOptions = FileToolOptions.Empty;

    public string Role { get; private set; } = "hermes";

    public HermesAIGAgent() : base()
    {
    }

    public void InitializeRole(string? role)
    {
        var value = (role ?? string.Empty).Trim();
        Role = value.Length == 0 ? "hermes" : value;
    }

    internal void ApplyToolOptions(FileToolOptions options)
    {
        _fileToolOptions = options ?? FileToolOptions.Empty;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"HermesAIGAgent({Role})");

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);
        await RegisterToolAsync(new FileReadTool(_fileToolOptions), cancellationToken: cancellationToken);
        await RegisterToolAsync(new FileWriteTool(_fileToolOptions), cancellationToken: cancellationToken);
    }
}
