namespace Aevatar.Workshop;

public sealed class WorkshopOptions
{
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
    public float Temperature { get; set; } = 0.6f;
    public int MaxOutputTokens { get; set; } = 1200;
    public int StreamChunkEveryN { get; set; } = 1;
    public int MaxSnapshotMessages { get; set; } = 60;
    public string AgentRole { get; set; } = "workshop_default";
    public bool EnableAgentYaml { get; set; } = true;
    public string WorkflowName { get; set; } = "workshop_mesh";
    public bool EnableWorkflowYaml { get; set; } = true;
    public List<string> DotNetToolDirectories { get; set; } = new();
    public int DotNetToolMaxFiles { get; set; } = 64;
}
