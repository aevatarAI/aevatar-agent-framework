using Aevatar.Agents.Cognitive.DependencyInjection;
using Aevatar.Agents.Cognitive.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

[Collection("EnvVarNonParallel")]
public class ServiceCollectionExtensionsTests
{
    private const string ConfigDirEnv = "AEVATAR_CONFIG_DIR";

    [Fact]
    public void AddCognitiveAgents_ShouldLoadWorkflows_FromDefaultAevatarDirectory()
    {
        var temp = MakeTempDir();
        var workflowsDir = Path.Combine(temp, "workflows");
        Directory.CreateDirectory(workflowsDir);

        var workflowPath = Path.Combine(workflowsDir, "hello.yaml");
        File.WriteAllText(workflowPath, @"
name: hello
steps:
  - id: respond
    type: llm_call
    prompt: ""hi""
    output: text
output:
  result: ""{{response}}""
");

        var oldEnv = Environment.GetEnvironmentVariable(ConfigDirEnv);
        try
        {
            Environment.SetEnvironmentVariable(ConfigDirEnv, temp);

            var services = new ServiceCollection();
            services.AddCognitiveAgents();

            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<IWorkflowRegistry>();

            registry.Get("hello").ShouldNotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConfigDirEnv, oldEnv);
            TryDeleteDir(temp);
        }
    }

    private static string MakeTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aevatar_cognitive_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
