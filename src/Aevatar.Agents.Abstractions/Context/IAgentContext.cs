namespace Aevatar.Agents.Abstractions.Context;

/// <summary>
/// Agent execution context for passing request-scoped data across agent boundaries.
/// Runtime agnostic - works across Local, Orleans, and ProtoActor.
/// </summary>
public interface IAgentContext
{
    /// <summary>
    /// Get context value by key.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="key">Context key</param>
    /// <returns>Value or default if not found</returns>
    T? Get<T>(AgentContextKey<T> key);

    /// <summary>
    /// Get context value by string key (for interop with Orleans RequestContext).
    /// </summary>
    /// <param name="key">String key</param>
    /// <returns>Value or null if not found</returns>
    object? Get(string key);

    /// <summary>
    /// Set context value.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="key">Context key</param>
    /// <param name="value">Value to set</param>
    void Set<T>(AgentContextKey<T> key, T value);

    /// <summary>
    /// Set context value by string key (for interop with Orleans RequestContext).
    /// </summary>
    /// <param name="key">String key</param>
    /// <param name="value">Value to set</param>
    void Set(string key, object? value);

    /// <summary>
    /// Remove context value.
    /// </summary>
    /// <param name="key">Context key</param>
    void Remove(string key);

    /// <summary>
    /// Clear all context values.
    /// </summary>
    void Clear();

    /// <summary>
    /// Get all context entries as dictionary (for serialization).
    /// </summary>
    IReadOnlyDictionary<string, object?> GetAll();

    /// <summary>
    /// Import context entries from dictionary (for deserialization).
    /// </summary>
    void Import(IReadOnlyDictionary<string, object?> entries);
}

