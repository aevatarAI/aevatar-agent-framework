using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Default IAgentContext implementation using ConcurrentDictionary for thread-safe access.
/// Optimized for high-concurrency scenarios with lock-free read operations.
/// </summary>
public class AsyncLocalAgentContext : IAgentContext
{
    private readonly ConcurrentDictionary<string, object?> _data = new();

    /// <inheritdoc />
    public T? Get<T>(AgentContextKey<T> key)
    {
        if (!_data.TryGetValue(key.Name, out var value) || value is null)
            return key.DefaultValue;

        if (value is T typedValue)
            return typedValue;

        // 兼容跨边界后的类型退化 (int->long, float->double, etc.)
        return AgentContextValueConverter.TryConvert(value, out T converted)
            ? converted
            : key.DefaultValue;
    }

    /// <inheritdoc />
    public object? Get(string key)
    {
        return _data.TryGetValue(key, out var value) ? value : null;
    }

    /// <inheritdoc />
    public void Set<T>(AgentContextKey<T> key, T value)
    {
        _data[key.Name] = value;
    }

    /// <inheritdoc />
    public void Set(string key, object? value)
    {
        _data[key] = value;
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        _data.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _data.Clear();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> GetAll()
    {
        // Return snapshot - ConcurrentDictionary enumeration is thread-safe
        // but we need a point-in-time snapshot for serialization
        return new Dictionary<string, object?>(_data);
    }

    /// <inheritdoc />
    public void Import(IReadOnlyDictionary<string, object?> entries)
    {
        foreach (var (key, value) in entries)
        {
            _data[key] = value;
        }
    }

    /// <summary>
    /// Gets the current count of items in the context.
    /// </summary>
    public int Count => _data.Count;

    /// <summary>
    /// Checks if the context contains a specific key.
    /// </summary>
    public bool ContainsKey(string key) => _data.ContainsKey(key);
}
