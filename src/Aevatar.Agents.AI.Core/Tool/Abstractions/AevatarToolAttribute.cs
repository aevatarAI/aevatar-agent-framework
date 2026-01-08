using System;

namespace Aevatar.Agents.AI.Tool.Abstractions;

/// <summary>
/// Marks tool class, supports automatic scanning and registration
/// <para/>
/// Usage example:
/// <code>
/// [AevatarTool(
///     Name = "send_email",
///     Description = "Send email to recipient",
///     Category = ToolCategory.Communication,
///     AutoRegister = true
/// )]
/// public class EmailTool : AevatarToolBase
/// {
///     // Tool implementation...
/// }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class AevatarToolAttribute : Attribute
{
    /// <summary>
    /// Tool name (unique identifier)
    /// <para/>If not specified, uses class name (removing "Tool" suffix)
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Tool description
    /// <para/>Describes tool purpose and usage scenarios
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Tool category
    /// <para/>Used for tool grouping and permission control
    /// </summary>
    public ToolCategory Category { get; set; } = ToolCategory.Custom;

    /// <summary>
    /// Whether to auto-register
    /// <para/>If true, tool scanner will automatically discover and register this tool
    /// </summary>
    public bool AutoRegister { get; set; } = true;

    /// <summary>
    /// Tool tags
    /// <para/>Used for tool search and classification
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Tool version
    /// <para/>Follows Semantic Versioning (SemVer)
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Whether confirmation is required
    /// <para/>If true, user confirmation required before execution (dangerous operation)
    /// </summary>
    public bool RequiresConfirmation { get; set; } = false;

    /// <summary>
    /// Whether is a dangerous operation
    /// <para/>Used to mark operations that may affect system security
    /// </summary>
    public bool IsDangerous { get; set; } = false;

    /// <summary>
    /// Whether internal access permission is required
    /// <para/>If true, can only be called by system internally
    /// </summary>
    public bool RequiresInternalAccess { get; set; } = false;

    /// <summary>
    /// Whether can be overridden
    /// <para/>If true, allows subclasses or configuration to override this tool
    /// </summary>
    public bool CanBeOverridden { get; set; } = true;
}
