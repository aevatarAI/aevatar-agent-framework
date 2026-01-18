namespace Aevatar.Platform.Core.Config;

// ============================================================
//  Aevatar Platform Config Models (local-only POCOs)
//
//  注意：
//  - 这些类型是 Platform 内部使用（读取 ~/.aevatar/config.json + secrets.json）。
//  - 不跨 runtime/stream 边界，因此不需要 Protobuf。
//  - 任何会被持久化为“会话事件/跨进程传输”的类型必须用 Protobuf（另见 Contracts）。
// ============================================================

public sealed record AevatarEffectiveConfig(
    string ConfigDirectory,
    string ConfigPath,
    string SecretsPath,
    AevatarConfig Config,
    AevatarSecrets Secrets);

public sealed class AevatarConfig
{
    public string Version { get; set; } = "1.0";

    public ModelsConfig Models { get; set; } = new();

    public AgentsConfig Agents { get; set; } = new();

    public ToolsConfig Tools { get; set; } = new();

    public UiConfig Ui { get; set; } = new();

    public LoggingConfig Logging { get; set; } = new();
}

public sealed class ModelsConfig
{
    public string? DefaultProvider { get; set; }

    public string? DefaultModel { get; set; }

    // providerName -> provider config (endpoint, etc). api keys come from secrets.json.
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderConfig
{
    public string? Endpoint { get; set; }

    public string? DefaultModel { get; set; }
}

public sealed class AgentsConfig
{
    public string DefaultWorkflow { get; set; } = "hermes";

    public string DefaultProfile { get; set; } = "coding";

    public int ParallelLimit { get; set; } = 3;
}

public sealed class ToolsConfig
{
    public ShellToolConfig Shell { get; set; } = new();

    public FileSystemToolConfig FileSystem { get; set; } = new();

    public ToolPluginsConfig Plugins { get; set; } = new();
}

public sealed class ShellToolConfig
{
    public List<string> AllowedCommands { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class FileSystemToolConfig
{
    public List<string> AllowedPaths { get; set; } = new();
}

public sealed class ToolPluginsConfig
{
    public bool Enabled { get; set; } = true;

    public bool IncludeDotNetFileTools { get; set; } = true;

    public bool IncludePythonFileTools { get; set; }

    public bool RequireManifestMarker { get; set; } = true;

    public int MaxFilesPerType { get; set; } = 64;

    public List<string> Directories { get; set; } = new();
}

public sealed class UiConfig
{
    public string Theme { get; set; } = "dark";

    public string? Editor { get; set; }
}

public sealed class LoggingConfig
{
    public string Level { get; set; } = "info";

    public string? File { get; set; }
}

// ------------------------------------------------------------
// Secrets (NEVER log / never include in session events)
// ------------------------------------------------------------

public sealed class AevatarSecrets
{
    public ProvidersSecrets Providers { get; set; } = new();

    public McpSecrets Mcp { get; set; } = new();
}

public sealed class ProvidersSecrets
{
    // providerName -> api_key
    public Dictionary<string, string> ApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class McpSecrets
{
    // serverName -> token / connection string (string-only; do not persist elsewhere)
    public Dictionary<string, string> Credentials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


