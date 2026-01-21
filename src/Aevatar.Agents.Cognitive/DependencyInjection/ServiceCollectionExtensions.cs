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
            if (!string.IsNullOrEmpty(options.WorkflowsDirectory))
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
