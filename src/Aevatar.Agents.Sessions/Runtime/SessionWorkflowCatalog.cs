using Aevatar.Agents.AI.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions.Runtime;

public interface IWorkflowCatalog
{
    string ResolveWorkflowName(string? name);
    string EnsureRoleWorkflowYaml(string role);
}

public sealed class SessionWorkflowCatalog : IWorkflowCatalog
{
    // ============================================================
    // 中文 + ASCII:
    // - Workflow 名称/路径/模板统一入口
    // - Session Runtime 只关心“要启动哪个 workflow”
    // ============================================================

    private readonly IOptionsMonitor<SessionRuntimeOptions> _runtimeOptions;
    private readonly IOptionsMonitor<CognitiveSessionOptions> _sessionOptions;

    public SessionWorkflowCatalog(
        IOptionsMonitor<SessionRuntimeOptions> runtimeOptions,
        IOptionsMonitor<CognitiveSessionOptions> sessionOptions)
    {
        _runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
        _sessionOptions = sessionOptions ?? throw new ArgumentNullException(nameof(sessionOptions));
    }

    public string ResolveWorkflowName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length > 0)
            return trimmed;

        var fallback = (_runtimeOptions.CurrentValue.WorkflowName ?? string.Empty).Trim();
        if (fallback.Length > 0)
            return fallback;

        return "workspace_mesh";
    }

    public string EnsureRoleWorkflowYaml(string role)
    {
        var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("role is required.", nameof(role));

        var dir = ResolveWorkflowDirectory();
        Directory.CreateDirectory(dir);

        var workflowName = $"role_{SanitizeFileName(key)}";
        var path = Path.Combine(dir, $"{workflowName}.yaml");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, BuildSingleRoleWorkflowYaml(key));
        }

        return workflowName;
    }

    private string ResolveWorkflowDirectory()
    {
        var dir = (_sessionOptions.CurrentValue.WorkflowsDirectory ?? string.Empty).Trim();
        if (dir.Length > 0)
            return ExpandHome(dir);

        var configDir = AgentYamlConfigLoader.GetDefaultConfigDirectory();
        return Path.Combine(configDir, "workflows");
    }

    private static string ExpandHome(string path)
    {
        var p = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (!p.StartsWith("~/", StringComparison.Ordinal))
            return p;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, p[2..]);
    }

    private static string BuildSingleRoleWorkflowYaml(string role)
    {
        var id = role.Trim();
        if (id.Length == 0)
            id = "role";

        return $$"""
dsl_version: "0.1"
goal:
  name: "Role Session"
  success_metric: "ok"
strategy: cot
budget:
  max_steps: 1
  token_limit: 512
nodes:
  - id: {{id}}
    type: "{{id}}"
edges: []
constraints: []
""";
    }

    private static string SanitizeFileName(string value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length == 0)
            return "workflow";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        name = name.Replace('/', '_').Replace('\\', '_');
        return name;
    }
}
