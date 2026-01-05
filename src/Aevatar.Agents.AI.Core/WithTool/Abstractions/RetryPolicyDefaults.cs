namespace Aevatar.Agents.AI.WithTool.Abstractions;

/// <summary>
/// Default values for retry policy configuration
/// </summary>
public static class RetryPolicyDefaults
{
    /// <summary>
    /// Default maximum number of retries
    /// </summary>
    public const int MaxRetries = 3;
    
    /// <summary>
    /// Default retry delay in milliseconds
    /// </summary>
    public const int RetryDelayMs = 1000;
    
    /// <summary>
    /// Default maximum retry delay in milliseconds
    /// </summary>
    public const int MaxRetryDelayMs = 30000;
}
