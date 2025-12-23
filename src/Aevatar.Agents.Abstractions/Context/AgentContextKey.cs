namespace Aevatar.Agents.Abstractions.Context;

/// <summary>
/// Type-safe context key for agent context values.
/// Prevents key collisions and ensures type safety at compile time.
/// </summary>
/// <typeparam name="T">Value type</typeparam>
public sealed class AgentContextKey<T> : IEquatable<AgentContextKey<T>>
{
    /// <summary>
    /// Key name (used for serialization and lookup).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Default value when key is not found.
    /// </summary>
    public T? DefaultValue { get; }

    /// <summary>
    /// Creates a new context key with the specified name.
    /// </summary>
    /// <param name="name">Key name for serialization and lookup</param>
    /// <param name="defaultValue">Default value when key is not found</param>
    public AgentContextKey(string name, T? defaultValue = default)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DefaultValue = defaultValue;
    }

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <inheritdoc />
    public override int GetHashCode() => Name.GetHashCode();

    /// <inheritdoc />
    public bool Equals(AgentContextKey<T>? other) =>
        other is not null && Name == other.Name;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is AgentContextKey<T> other && Equals(other);

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(AgentContextKey<T>? left, AgentContextKey<T>? right) =>
        left?.Equals(right) ?? right is null;

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(AgentContextKey<T>? left, AgentContextKey<T>? right) =>
        !(left == right);
}

