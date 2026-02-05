namespace Aevatar.VibeResearching.UserProviders.ValueObjects;

/// <summary>
/// Parses and represents a namespaced provider reference.
/// Format: "user:{id}" or "platform:{name}" or "{name}" (backward compat = platform).
/// </summary>
public sealed record ProviderNamespace
{
    public const string UserPrefix = "user:";
    public const string PlatformPrefix = "platform:";

    /// <summary>"user" or "platform".</summary>
    public string Scope { get; }

    /// <summary>Provider ID (for user) or name (for platform).</summary>
    public string Identifier { get; }

    private ProviderNamespace(string scope, string identifier)
    {
        Scope = scope;
        Identifier = identifier;
    }

    /// <summary>Whether this references a user-level provider.</summary>
    public bool IsUser => string.Equals(Scope, "user", StringComparison.Ordinal);

    /// <summary>Whether this references a platform-level provider.</summary>
    public bool IsPlatform => string.Equals(Scope, "platform", StringComparison.Ordinal);

    /// <summary>
    /// Parses a raw namespace string into scope and identifier.
    /// Unprefixed strings are treated as platform providers for backward compatibility.
    /// </summary>
    public static ProviderNamespace Parse(string raw)
    {
        var value = (raw ?? string.Empty).Trim();

        if (value.StartsWith(UserPrefix, StringComparison.Ordinal))
            return new ProviderNamespace("user", value[UserPrefix.Length..]);

        if (value.StartsWith(PlatformPrefix, StringComparison.Ordinal))
            return new ProviderNamespace("platform", value[PlatformPrefix.Length..]);

        // Backward compat: unprefixed = platform
        return new ProviderNamespace("platform", value);
    }

    /// <summary>Creates a user-scoped namespace.</summary>
    public static ProviderNamespace ForUser(string id) => new("user", id);

    /// <summary>Creates a platform-scoped namespace.</summary>
    public static ProviderNamespace ForPlatform(string name) => new("platform", name);

    public override string ToString() => $"{Scope}:{Identifier}";
}
