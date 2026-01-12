namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 流式Token
/// </summary>
public class AevatarLLMToken
{
    public string Content { get; set; } = string.Empty;
    public int Index { get; set; }
    public bool IsComplete { get; set; }
    public string? ReasoningContent { get; set; }
    public AevatarFunctionCall? AevatarFunctionCall { get; set; }
}