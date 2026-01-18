using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tools;

namespace Aevatar.Platform.Core.Workflow;

// ============================================================
//  PlatformRoleAIGAgent
//
//  说明：
//  - Platform 默认 role agent
//  - 注册 coding agent 常用工具集合（受 allowlist 管控）
// ============================================================
public sealed class PlatformRoleAIGAgent : AIGAgentBase
{
    private FileToolOptions _fileOptions = FileToolOptions.Empty;
    private CommandToolOptions _commandOptions = CommandToolOptions.Empty;

    public string Role { get; private set; } = "role";

    public void InitializeRole(string? role)
    {
        var value = (role ?? string.Empty).Trim();
        Role = value.Length == 0 ? "role" : value;
    }

    internal void ConfigureTools(FileToolOptions fileOptions, CommandToolOptions commandOptions)
    {
        _fileOptions = fileOptions ?? FileToolOptions.Empty;
        _commandOptions = commandOptions ?? CommandToolOptions.Empty;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"PlatformRoleAIGAgent({Role})");

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);
        await RegisterFileToolsAsync(cancellationToken);
        await RegisterSearchToolsAsync(cancellationToken);
        await RegisterProcessToolsAsync(cancellationToken);
        await RegisterGitToolsAsync(cancellationToken);
        await RegisterUtilityToolsAsync(cancellationToken);
    }

    private async Task RegisterFileToolsAsync(CancellationToken ct)
    {
        await RegisterToolAsync(new FileReadTool(_fileOptions), cancellationToken: ct);
        await RegisterToolAsync(new FileWriteTool(_fileOptions), cancellationToken: ct);
        await RegisterToolAsync(new FileDeleteTool(_fileOptions), cancellationToken: ct);
        await RegisterToolAsync(new PathExistsTool(_fileOptions), cancellationToken: ct);
        await RegisterToolAsync(new DirListTool(_fileOptions), cancellationToken: ct);
        await RegisterToolAsync(new FileStatTool(_fileOptions), cancellationToken: ct);
        await RegisterToolAsync(new HashSha256Tool(_fileOptions), cancellationToken: ct);
    }

    private async Task RegisterSearchToolsAsync(CancellationToken ct)
    {
        await RegisterToolAsync(new GlobTool(_fileOptions), cancellationToken: ct);
        await RegisterToolAsync(new GrepTool(_fileOptions), cancellationToken: ct);
        await RegisterToolAsync(new AstGrepTool(_commandOptions), cancellationToken: ct);
    }

    private async Task RegisterProcessToolsAsync(CancellationToken ct)
    {
        await RegisterToolAsync(new BashTool(_commandOptions), cancellationToken: ct);
        await RegisterToolAsync(new LspTool(_commandOptions), cancellationToken: ct);
    }

    private async Task RegisterGitToolsAsync(CancellationToken ct)
    {
        await RegisterToolAsync(new GitStatusTool(_commandOptions), cancellationToken: ct);
        await RegisterToolAsync(new GitDiffTool(_commandOptions), cancellationToken: ct);
        await RegisterToolAsync(new GitCommitTool(_commandOptions), cancellationToken: ct);
    }

    private async Task RegisterUtilityToolsAsync(CancellationToken ct)
    {
        await RegisterToolAsync(new TimeNowTool(), cancellationToken: ct);
        await RegisterToolAsync(new EnvGetTool(), cancellationToken: ct);
        await RegisterToolAsync(new UuidTool(), cancellationToken: ct);
        await RegisterToolAsync(new Base64EncodeTool(), cancellationToken: ct);
        await RegisterToolAsync(new Base64DecodeTool(), cancellationToken: ct);
        await RegisterToolAsync(new JsonFormatTool(), cancellationToken: ct);
        await RegisterToolAsync(new JsonValidateTool(), cancellationToken: ct);
    }
}
