using System.Collections.Generic;

namespace Aevatar.Platform.Core.Config;

// ============================================================
//  SecretsKeyRules
//
//  说明：
//  - 统一 secrets.json 里的 key 规则，避免散落在各处
// ============================================================
public static class SecretsKeyRules
{
    public const string LlmDefaultProviderKey = "LLMProviders:Default";
    public const string LlmProvidersPrefix = "LLMProviders:Providers:";
    public const string LlmProviderApiKeySuffix = ":ApiKey";

    public const string McpServersPrefix = "MCP:Servers:";
    public const string McpTokenSuffix = ":Token";

    public static string BuildProviderApiKeyKey(string provider)
        => $"{LlmProvidersPrefix}{provider}{LlmProviderApiKeySuffix}";

    public static bool TryParseProviderApiKeyKey(string key, out string provider)
    {
        provider = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!key.StartsWith(LlmProvidersPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!key.EndsWith(LlmProviderApiKeySuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var name = key.Substring(
                LlmProvidersPrefix.Length,
                key.Length - LlmProvidersPrefix.Length - LlmProviderApiKeySuffix.Length)
            .Trim();
        if (name.Length == 0)
            return false;

        provider = name;
        return true;
    }

    public static bool TryParseProviderFieldKey(string key, out string provider, out string field)
    {
        provider = string.Empty;
        field = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
            return false;
        if (!key.StartsWith(LlmProvidersPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = key[LlmProvidersPrefix.Length..];
        var parts = remainder.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        provider = parts[0].Trim();
        field = parts[1].Trim();
        if (provider.Length == 0 || field.Length == 0)
            return false;

        return true;
    }

    public static string BuildMcpTokenKey(string server)
        => $"{McpServersPrefix}{server}{McpTokenSuffix}";

    public static string BuildMcpLegacyKey(string server)
        => $"{McpServersPrefix}{server}";

    public static bool TryParseMcpServerKey(string key, out string server, out string field)
    {
        server = string.Empty;
        field = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
            return false;
        if (!key.StartsWith(McpServersPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = key[McpServersPrefix.Length..];
        var parts = remainder.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        server = parts[0].Trim();
        if (server.Length == 0)
            return false;

        field = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        return true;
    }

    public static bool TryParseMcpTokenKey(string key, out string server)
    {
        server = string.Empty;
        if (!TryParseMcpServerKey(key, out var name, out var field))
            return false;

        if (string.IsNullOrWhiteSpace(field) ||
            field.Equals("Token", StringComparison.OrdinalIgnoreCase))
        {
            server = name;
            return true;
        }

        return false;
    }

    public static bool HasMcpCredential(IReadOnlyDictionary<string, string> secrets, string server)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var name = (server ?? string.Empty).Trim();
        if (name.Length == 0)
            return false;

        return secrets.ContainsKey(BuildMcpTokenKey(name)) ||
               secrets.ContainsKey(BuildMcpLegacyKey(name));
    }

    public static List<string> ListProvidersWithApiKey(IReadOnlyDictionary<string, string> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in secrets.Keys)
        {
            if (TryParseProviderApiKeyKey(key, out var name))
                results.Add(name);
        }

        return results.ToList();
    }
}
