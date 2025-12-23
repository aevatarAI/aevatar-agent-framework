namespace Aevatar.Agents.Abstractions.Context;

/// <summary>
/// Options for controlling which context keys are propagated through EventEnvelope.
/// </summary>
public class AgentContextPropagationOptions
{
    /// <summary>
    /// Maximum number of keys allowed in context metadata.
    /// Default: 32
    /// </summary>
    public int MaxKeys { get; set; } = 32;

    /// <summary>
    /// Maximum total serialized bytes allowed.
    /// Default: 8192 (8KB)
    /// </summary>
    public int MaxTotalBytes { get; set; } = 8192;

    /// <summary>
    /// Keys to always propagate. If null or empty, all keys are propagated
    /// (subject to MaxKeys and MaxTotalBytes limits).
    /// </summary>
    public HashSet<string>? AllowedKeys { get; set; }

    /// <summary>
    /// Keys to never propagate (takes precedence over AllowedKeys).
    /// Useful for excluding sensitive data.
    /// </summary>
    public HashSet<string>? DeniedKeys { get; set; }

    /// <summary>
    /// Check if a key should be propagated.
    /// </summary>
    public bool ShouldPropagate(string key)
    {
        // Denied keys always take precedence
        if (DeniedKeys?.Contains(key) == true)
            return false;

        // If allowlist is set, only allow those keys
        if (AllowedKeys?.Count > 0)
            return AllowedKeys.Contains(key);

        // Default: allow all keys
        return true;
    }

    /// <summary>
    /// Default options - propagate all keys with size limits.
    /// </summary>
    public static AgentContextPropagationOptions Default => new();

    /// <summary>
    /// Add keys to the allowed list.
    /// </summary>
    public AgentContextPropagationOptions Allow(params string[] keys)
    {
        AllowedKeys ??= new HashSet<string>();
        foreach (var key in keys)
            AllowedKeys.Add(key);
        return this;
    }

    /// <summary>
    /// Add keys to the denied list.
    /// </summary>
    public AgentContextPropagationOptions Deny(params string[] keys)
    {
        DeniedKeys ??= new HashSet<string>();
        foreach (var key in keys)
            DeniedKeys.Add(key);
        return this;
    }
}

