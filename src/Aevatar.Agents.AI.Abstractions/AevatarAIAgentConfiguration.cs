namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI Agent configuration
/// </summary>
public class AevatarAIAgentConfiguration
{
    public string Provider { get; set; } = "SemanticKernel";
    public string Model { get; set; } = "gpt-4";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 32000;
    public string? SystemPrompt { get; set; }
    public int? MaxChainOfAevatarThoughtSteps { get; set; }
    public int? MaxReActIterations { get; set; }

    // Tree of Thoughts configuration
    public int? MaxTreeDepth { get; set; } = 3;
    public int? TreeBranchingFactor { get; set; } = 3;
    public int? MaxTreeNodes { get; set; } = 20;

    // Tool configuration
    public bool? RecordToolExecutions { get; set; } = true;

    public List<string> EnabledTools { get; set; } = new();
    public Dictionary<string, object> ProviderSettings { get; set; } = new();
}