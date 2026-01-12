namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 参数定义
/// </summary>
public class AevatarParameterDefinition
{
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public bool Required { get; set; }
    public object? Default { get; set; }
    public IList<string>? Enum { get; set; }

    /// <summary>
    /// Array item schema (only meaningful when Type == "array").
    /// </summary>
    public AevatarParameterDefinition? Items { get; set; }
}