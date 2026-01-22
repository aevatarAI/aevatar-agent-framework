using Aevatar.Agents.AI.Core.Hooks;

namespace Aevatar.Agents.AI.Core.Hooks.External;

/// <summary>
/// External hook command binding.
/// </summary>
public sealed class ExternalHookCommand
{
    /// <summary>
    /// Hook stage to bind.
    /// </summary>
    public AevatarAgentHookStage Stage { get; set; }

    /// <summary>
    /// Executable path or command name.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Raw arguments string (optional).
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Working directory (optional).
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Timeout in milliseconds (0 uses default).
    /// </summary>
    public int TimeoutMs { get; set; }

    /// <summary>
    /// Toggle this command on/off.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
