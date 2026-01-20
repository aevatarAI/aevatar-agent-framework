using System.Collections.Generic;

namespace Aevatar.Agents.AI.Core.Hooks.External;

/// <summary>
/// Options for external hook runner (Cursor-style stdio JSON scripts).
/// </summary>
public sealed class AevatarExternalHookOptions
{
    /// <summary>
    /// External hook commands (per stage).
    /// </summary>
    public List<ExternalHookCommand> Commands { get; set; } = new();

    /// <summary>
    /// Default timeout in milliseconds for each command.
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 10_000;

    /// <summary>
    /// Whether to include ChatRequest.Message content in payload.
    /// Default: false (avoid leaking sensitive data).
    /// </summary>
    public bool IncludeChatMessageContent { get; set; }

    /// <summary>
    /// Max characters to include for ChatRequest.Message (when enabled).
    /// </summary>
    public int MaxMessageChars { get; set; } = 2_000;

    /// <summary>
    /// Max characters to include for tool output content.
    /// </summary>
    public int MaxToolResultChars { get; set; } = 2_000;
}
