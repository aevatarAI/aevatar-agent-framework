using System.IO;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Cognitive.DependencyInjection;

/// <summary>
/// Dependency injection extensions
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Cognitive Agent services
    /// </summary>
    public static IServiceCollection AddCognitiveAgents(
        this IServiceCollection services,
        Action<CognitiveAgentOptions>? configure = null)
    {
        var options = new CognitiveAgentOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.WorkflowsDirectory))
            options.WorkflowsDirectory = ResolveDefaultWorkflowsDirectory();
        
        // Register template engine
        services.AddSingleton<TemplateEngine>();
        services.AddSingleton<OutputParserFactory>();
        
        // Register workflow parser
        services.AddSingleton<WorkflowParser>();
        
        // Register workflow registry
        services.AddSingleton<IWorkflowRegistry>(sp =>
        {
            var registry = new InMemoryWorkflowRegistry();
            
            // Load built-in workflows
            if (options.LoadBuiltInWorkflows)
            {
                LoadBuiltInWorkflows(registry);
            }
            
            // Load workflows from directory
            if (!string.IsNullOrWhiteSpace(options.WorkflowsDirectory) &&
                Directory.Exists(options.WorkflowsDirectory))
            {
                var parser = sp.GetRequiredService<WorkflowParser>();
                foreach (var workflow in parser.ParseDirectory(options.WorkflowsDirectory))
                {
                    registry.Register(workflow);
                }
            }
            
            return registry;
        });
        
        return services;
    }
    
    private static void LoadBuiltInWorkflows(InMemoryWorkflowRegistry registry)
    {
        // Direct strategy
        registry.Register(new WorkflowDefinition
        {
            Name = "direct",
            Version = "",
            Description = "Single LLM call",
            Inputs =
            [
                new InputParameter { Name = "task", Type = "string", Required = true },
                new InputParameter { Name = "system_prompt", Type = "string", DefaultValue = "You are a helpful AI assistant." }
            ],
            Steps =
            [
                new StepDefinition
                {
                    Id = "respond",
                    Type = "llm_call",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["prompt"] = "{{task}}",
                        ["system"] = "{{system_prompt}}",
                        ["output"] = "text"
                    },
                    Store = "response"
                }
            ],
            Output = new Dictionary<string, string>
            {
                ["result"] = "{{response}}"
            }
        });
    }

    private static string ResolveDefaultWorkflowsDirectory()
    {
        var configDir = ResolveAevatarConfigDirectory();
        return Path.Combine(configDir, "workflows");
    }

    private static string ResolveAevatarConfigDirectory()
    {
        var fromEnv = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG_DIR") ?? string.Empty).Trim();
        if (fromEnv.Length > 0)
            return ExpandHome(fromEnv);

        var secretsDir = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS_DIR") ?? string.Empty).Trim();
        if (secretsDir.Length > 0)
            return ExpandHome(secretsDir);

        var secretsPath = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS_PATH") ?? string.Empty).Trim();
        if (secretsPath.Length > 0)
            return Path.GetDirectoryName(ExpandHome(secretsPath)) ?? ExpandHome(secretsPath);

        var legacySecrets = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS") ?? string.Empty).Trim();
        if (legacySecrets.Length > 0)
            return Path.GetDirectoryName(ExpandHome(legacySecrets)) ?? ExpandHome(legacySecrets);

        var configPath = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG") ?? string.Empty).Trim();
        if (configPath.Length > 0)
            return Path.GetDirectoryName(ExpandHome(configPath)) ?? ExpandHome(configPath);

        var configPath2 = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG_PATH") ?? string.Empty).Trim();
        if (configPath2.Length > 0)
            return Path.GetDirectoryName(ExpandHome(configPath2)) ?? ExpandHome(configPath2);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar");
    }

    private static string ExpandHome(string path)
    {
        var p = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (!p.StartsWith("~/", StringComparison.Ordinal))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, p[2..]);
    }
}

/// <summary>
/// Cognitive Agent configuration options
/// </summary>
public class CognitiveAgentOptions
{
    /// <summary>Whether to load built-in workflows</summary>
    public bool LoadBuiltInWorkflows { get; set; } = true;
    
    /// <summary>Workflow directory (for hot reload)</summary>
    public string? WorkflowsDirectory { get; set; }
    
    /// <summary>Default maximum recursion depth</summary>
    public int MaxRecursionDepth { get; set; } = 10;
}
