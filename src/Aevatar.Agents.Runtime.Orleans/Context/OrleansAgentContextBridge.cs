using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Orleans.Runtime;

namespace Aevatar.Agents.Runtime.Orleans.Context;

/// <summary>
/// Bridge implementation that reads/writes Orleans RequestContext.
/// Provides IAgentContext interface over Orleans' built-in request context.
/// </summary>
public class OrleansAgentContextBridge : IAgentContext
{
    /// <inheritdoc />
    public T? Get<T>(AgentContextKey<T> key)
    {
        var value = RequestContext.Get(key.Name);
        if (value is null)
            return key.DefaultValue;

        if (value is T typedValue)
            return typedValue;

        return AgentContextValueConverter.TryConvert(value, out T converted)
            ? converted
            : key.DefaultValue;
    }

    /// <inheritdoc />
    public object? Get(string key)
    {
        return RequestContext.Get(key);
    }

    /// <inheritdoc />
    public void Set<T>(AgentContextKey<T> key, T value)
    {
        if (value != null)
        {
            RequestContext.Set(key.Name, value);
        }
        else
        {
            Remove(key.Name);
        }
    }

    /// <inheritdoc />
    public void Set(string key, object? value)
    {
        if (value != null)
        {
            RequestContext.Set(key, value);
        }
        else
        {
            Remove(key);
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        RequestContext.Remove(key);
    }

    /// <inheritdoc />
    public void Clear()
    {
        RequestContext.Clear();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> GetAll()
    {
        // Orleans 9.x 支持枚举 Entries/Keys
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in RequestContext.Entries)
        {
            result[key] = value;
        }
        return result;
    }

    /// <inheritdoc />
    public void Import(IReadOnlyDictionary<string, object?> entries)
    {
        foreach (var (key, value) in entries)
        {
            Set(key, value);
        }
    }
}

