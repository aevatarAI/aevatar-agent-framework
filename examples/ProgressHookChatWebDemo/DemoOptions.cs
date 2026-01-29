namespace ProgressHookChatWebDemo;

public sealed class DemoOptions
{
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
    public float Temperature { get; set; } = 0.7f;
    public int MaxOutputTokens { get; set; } = 1200;
    public int MaxSnapshotMessages { get; set; } = 50;
    public string AgentRole { get; set; } = "progress_demo";
    public bool EnableAgentYaml { get; set; } = true;
}
