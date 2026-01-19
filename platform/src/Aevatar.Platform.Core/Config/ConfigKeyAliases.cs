namespace Aevatar.Platform.Core.Config;

// ============================================================
//  ConfigKeyAliases
//
//  说明：
//  - 统一配置字段别名，避免散落在解析逻辑中
// ============================================================
public static class ConfigKeyAliases
{
    public static readonly string[] Root = ["Aevatar", "aevatar"];
    public static readonly string[] Version = ["Version", "version"];

    public static readonly string[] Models = ["Models", "models"];
    public static readonly string[] DefaultProvider = ["DefaultProvider", "default_provider"];
    public static readonly string[] DefaultModel = ["DefaultModel", "default_model"];
    public static readonly string[] Providers = ["Providers", "providers"];
    public static readonly string[] ProviderModel = ["DefaultModel", "default_model", "Model", "model"];
    public static readonly string[] ProviderEndpoint = ["Endpoint", "endpoint"];

    public static readonly string[] Agents = ["Agents", "agents"];
    public static readonly string[] DefaultWorkflow = ["DefaultWorkflow", "default_workflow"];
    public static readonly string[] DefaultProfile = ["DefaultProfile", "default_profile"];
    public static readonly string[] ParallelLimit = ["ParallelLimit", "parallel_limit"];

    public static readonly string[] Tools = ["Tools", "tools"];
    public static readonly string[] Shell = ["Shell", "shell"];
    public static readonly string[] FileSystem = ["FileSystem", "filesystem"];
    public static readonly string[] ToolPlugins = ["Plugins", "plugins", "ToolPlugins", "tool_plugins"];
    public static readonly string[] AllowedCommands = ["AllowedCommands", "allowed_commands"];
    public static readonly string[] TimeoutSeconds = ["TimeoutSeconds", "timeout_seconds"];
    public static readonly string[] AllowedPaths = ["AllowedPaths", "allowed_paths"];
    public static readonly string[] Enabled = ["Enabled", "enabled"];
    public static readonly string[] Directories = ["Directories", "directories", "Dirs", "dirs"];
    public static readonly string[] IncludeDotNetFileTools = ["IncludeDotNetFileTools", "include_dotnet_file_tools", "DotNet", "dotnet"];
    public static readonly string[] IncludePythonFileTools = ["IncludePythonFileTools", "include_python_file_tools", "Python", "python"];
    public static readonly string[] RequireManifestMarker = ["RequireManifestMarker", "require_manifest_marker", "ManifestMarker", "manifest_marker"];
    public static readonly string[] MaxFilesPerType = ["MaxFilesPerType", "max_files_per_type", "MaxFiles", "max_files"];

    public static readonly string[] Ui = ["Ui", "ui"];
    public static readonly string[] Theme = ["Theme", "theme"];
    public static readonly string[] Editor = ["Editor", "editor"];

    public static readonly string[] Logging = ["Logging", "logging"];
    public static readonly string[] Level = ["Level", "level"];
    public static readonly string[] File = ["File", "file"];
}
