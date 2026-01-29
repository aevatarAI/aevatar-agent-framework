namespace Aevatar.Agents.Workspaces.Core;

public sealed class RoleWorkspaceOptions
{
    public string RootRole { get; set; } = "sisyphus";
    public string WorkspaceSessionId { get; set; } = "role_workspace";
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
    public double Temperature { get; set; } = 0.6;
    public int MaxOutputTokens { get; set; } = 1200;
    public int MaxSnapshotMessages { get; set; } = 60;
    public bool EnableAgentYaml { get; set; } = true;
}
