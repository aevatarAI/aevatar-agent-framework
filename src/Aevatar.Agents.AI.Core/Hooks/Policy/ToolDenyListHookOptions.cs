using System.Collections.Generic;

namespace Aevatar.Agents.AI.Core.Hooks.Policy;

/// <summary>
/// Options for ToolDenyListHook.
/// </summary>
public sealed class ToolDenyListHookOptions
{
    /// <summary>
    /// Tool names to deny (case-insensitive).
    /// </summary>
    public List<string> DeniedTools { get; set; } = new();

    /// <summary>
    /// Optional reason to include in deny response.
    /// </summary>
    public string? DenyReason { get; set; }
}
