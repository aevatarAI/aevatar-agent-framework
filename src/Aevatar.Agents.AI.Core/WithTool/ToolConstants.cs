namespace Aevatar.Agents.AI.WithTool;

/// <summary>
/// Tool system constants to avoid magic strings and numbers
/// </summary>
public static class ToolConstants
{
    /// <summary>
    /// Default tool version
    /// </summary>
    public const string DefaultVersion = "1.0.0";
    
    /// <summary>
    /// Default parameter type
    /// </summary>
    public const string DefaultParameterType = "string";
    
    /// <summary>
    /// Default return type
    /// </summary>
    public const string DefaultReturnType = "object";
    
    /// <summary>
    /// Tool name prefix format for auto-generated tools
    /// </summary>
    public const string AutoToolNameFormat = "Tool_{0}";
    
    /// <summary>
    /// Default description for AI tools
    /// </summary>
    public const string DefaultAIToolDescription = "AI Tool";
    
    /// <summary>
    /// Placeholder message for tool execution
    /// </summary>
    public const string ToolExecutedMessageFormat = "Tool {0} executed";
    
    /// <summary>
    /// Placeholder message for function calls
    /// </summary>
    public const string FunctionCalledMessageFormat = "Function {0} called";
}
