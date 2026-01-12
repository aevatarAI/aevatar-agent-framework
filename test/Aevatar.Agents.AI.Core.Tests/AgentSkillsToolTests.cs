using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Tests.Fixtures;
using Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;
using Aevatar.Agents.AI.Core.Tests.TestAgents;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests;

public class AgentSkillsToolTests(AgentSkillsToolFixture fixture) : IClassFixture<AgentSkillsToolFixture>
{
    private readonly IGAgentFactory _agentFactory = fixture.GAgentFactory;
    private MockLLMProvider MockProvider => (MockLLMProvider)fixture.LLMProviderFactory.GetProvider("test-provider");

    private static (string RootDir, string SkillDir) CreateSkillRoot(
        string skillName,
        string description,
        IReadOnlyList<string> allowedTools,
        bool includeDotNetToolFile)
    {
        var root = Path.Combine(Path.GetTempPath(), "aevatar-agent-skills-tests", Guid.NewGuid().ToString("N"));
        var skillDir = Path.Combine(root, "skill-1");
        Directory.CreateDirectory(skillDir);

        var yaml = new List<string>
        {
            "---",
            $"name: {skillName}",
            $"description: {description}",
            "allowed-tools:"
        };
        yaml.AddRange(allowedTools.Select(t => $"- {t}"));
        yaml.Add("---");
        yaml.Add("BODY:");
        yaml.Add("This is the skill body.");
        yaml.Add("END.");

        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), string.Join('\n', yaml));

        if (includeDotNetToolFile)
        {
            var toolFile = Path.Combine(skillDir, "my_tool.cs");
            File.WriteAllText(toolFile,
                """
                /*aevatar_tool {
                  "name": "my_dotnet_tool",
                  "description": "Test dotnet tool"
                }*/
                using System;
                public static class Program { public static void Main() => Console.WriteLine("{}"); }
                """);
        }

        return (root, skillDir);
    }

    private static void CleanupDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup for tests
        }
    }

    [Fact]
    public async Task SkillsList_ShouldListSkills_AndReportDotNetToolsPresence()
    {
        var (root, _) = CreateSkillRoot(
            skillName: "MySkill",
            description: "My skill desc",
            allowedTools: new[] { "search_memory" },
            includeDotNetToolFile: true);

        try
        {
            var agent = _agentFactory.CreateGAgent<AgentSkillsToolTestAgent>("agent-skills-1");
            agent.AllowDangerousTools = true; // skills tools are dangerous by default
            await agent.ConfigureAgentSkillsAsync(new[] { root }, enable: true);

            var exec = await agent.ExecuteToolForTestAsync("skills_list");
            exec.IsSuccess.ShouldBeTrue();

            var content = exec.Content;
            content.ShouldNotBeNullOrWhiteSpace();
            content!.TrimStart().ShouldStartWith("{");
            using var doc = JsonDocument.Parse(content);
            var json = doc.RootElement;

            json.GetProperty("success").GetBoolean().ShouldBeTrue();
            json.GetProperty("enabled").GetBoolean().ShouldBeTrue();

            var roots = json.GetProperty("roots").EnumerateArray().Select(x => x.GetString()).ToArray();
            roots.ShouldContain(Path.GetFullPath(root));

            json.GetProperty("count").GetInt32().ShouldBe(1);

            var skills = json.GetProperty("skills");
            skills.GetArrayLength().ShouldBe(1);
            var s0 = skills[0];
            s0.GetProperty("name").GetString().ShouldBe("MySkill");
            s0.GetProperty("description").GetString().ShouldBe("My skill desc");
            s0.GetProperty("hasDotNetTools").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            CleanupDir(root);
        }
    }

    [Fact]
    public async Task SkillsLoad_ShouldReturnMarkdownBody_AndRegisterDotNetTools_WhenEnabled()
    {
        var (root, _) = CreateSkillRoot(
            skillName: "MySkill",
            description: "My skill desc",
            allowedTools: new[] { "search_memory" },
            includeDotNetToolFile: true);

        try
        {
            var agent = _agentFactory.CreateGAgent<AgentSkillsToolTestAgent>("agent-skills-2");
            agent.AllowDangerousTools = true;
            await agent.ConfigureAgentSkillsAsync(new[] { root }, enable: true);

            var exec = await agent.ExecuteToolForTestAsync("skills_load", new Dictionary<string, object>
            {
                ["name"] = "MySkill",
                ["register_tools"] = true,
                ["max_chars"] = 16000
            });

            exec.IsSuccess.ShouldBeTrue();

            var content = exec.Content;
            content.ShouldNotBeNullOrWhiteSpace();
            content!.TrimStart().ShouldStartWith("{");
            using var doc = JsonDocument.Parse(content);
            var json = doc.RootElement;
            json.GetProperty("success").GetBoolean().ShouldBeTrue();
            json.GetProperty("name").GetString().ShouldBe("MySkill");

            var markdown = json.GetProperty("markdown").GetString();
            markdown.ShouldNotBeNull();
            markdown!.ShouldContain("This is the skill body.");

            var registered = json.GetProperty("registeredTools").EnumerateArray().Select(x => x.GetString()).ToArray();
            registered.ShouldContain("my_dotnet_tool");

            var tools = await agent.GetRegisteredToolsAsync();
            tools.Any(t => string.Equals(t.Name, "my_dotnet_tool", StringComparison.OrdinalIgnoreCase))
                .ShouldBeTrue();
        }
        finally
        {
            CleanupDir(root);
        }
    }

    [Fact]
    public async Task SkillsLoad_ShouldReturnError_WhenNameMissing_OrSkillNotFound()
    {
        var (root, _) = CreateSkillRoot(
            skillName: "MySkill",
            description: "My skill desc",
            allowedTools: Array.Empty<string>(),
            includeDotNetToolFile: false);

        try
        {
            var agent = _agentFactory.CreateGAgent<AgentSkillsToolTestAgent>("agent-skills-3");
            agent.AllowDangerousTools = true;
            await agent.ConfigureAgentSkillsAsync(new[] { root }, enable: true);

            var missingName = await agent.ExecuteToolForTestAsync("skills_load", new Dictionary<string, object>());
            missingName.Content.ShouldNotBeNullOrWhiteSpace();
            missingName.Content!.TrimStart().ShouldStartWith("{");
            using (var doc = JsonDocument.Parse(missingName.Content))
            {
                var json = doc.RootElement;
                json.GetProperty("success").GetBoolean().ShouldBeFalse();
                var error = json.GetProperty("error").GetString();
                error.ShouldNotBeNull();
                error!.ShouldContain("Parameter 'name' is required");
            }

            var notFound = await agent.ExecuteToolForTestAsync("skills_load", new Dictionary<string, object>
            {
                ["name"] = "NoSuchSkill"
            });
            notFound.Content.ShouldNotBeNullOrWhiteSpace();
            notFound.Content!.TrimStart().ShouldStartWith("{");
            using (var doc = JsonDocument.Parse(notFound.Content))
            {
                var json = doc.RootElement;
                json.GetProperty("success").GetBoolean().ShouldBeFalse();
                var error = json.GetProperty("error").GetString();
                error.ShouldNotBeNull();
                error!.ShouldContain("not found");
                var available = json.GetProperty("available").EnumerateArray().Select(x => x.GetString()).ToArray();
                available.ShouldContain("MySkill");
            }
        }
        finally
        {
            CleanupDir(root);
        }
    }

    [Fact]
    public async Task ChatAsync_WithSkillsLoad_ShouldApplyToolAllowlist_AndDenyDisallowedToolExecution()
    {
        // Arrange: skill restricts tools to search_memory only.
        var (root, _) = CreateSkillRoot(
            skillName: "MySkill",
            description: "My skill desc",
            allowedTools: new[] { "search_memory" },
            includeDotNetToolFile: false);

        try
        {
            MockProvider.Clear();
            MockProvider.EnqueueResponse(new AevatarLLMResponse
            {
                AevatarFunctionCall = new AevatarFunctionCall
                {
                    Name = "skills_load",
                    Arguments = "{\"name\":\"MySkill\",\"register_tools\":false}"
                }
            });
            MockProvider.EnqueueResponse(new AevatarLLMResponse
            {
                AevatarFunctionCall = new AevatarFunctionCall
                {
                    Name = "publish_event",
                    Arguments = "{}"
                }
            });
            MockProvider.EnqueueResponse(new AevatarLLMResponse { Content = "done", AevatarStopReason = AevatarStopReason.Complete });

            var agent = _agentFactory.CreateGAgent<TestAIGAgent>("agent-skills-4");
            await agent.InitializeAsync("test-provider");

            agent.AllowInternalTools = true;
            agent.AllowDangerousTools = true; // must be on to isolate allowlist denial (not policy denial)
            await agent.ConfigureAgentSkillsAsync(new[] { root }, enable: true);

            // Act
            var response = await agent.ChatAsync(ChatRequest.Create("hi"));

            // Assert: final content and last tool call is denied by allowlist
            response.Content.ShouldBe("done");
            response.ToolCalled.ShouldBeTrue();
            response.ToolCall.ShouldNotBeNull();
            response.ToolCall!.ToolName.ShouldBe("publish_event");

            response.ToolCall.Result.ShouldNotBeNullOrWhiteSpace();
            response.ToolCall.Result!.TrimStart().ShouldStartWith("{");
            using (var doc = JsonDocument.Parse(response.ToolCall.Result))
            {
                var json = doc.RootElement;
                json.GetProperty("success").GetBoolean().ShouldBeFalse();
                var error = json.GetProperty("error").GetString();
                error.ShouldNotBeNull();
                error!.ShouldContain("allowlist");
                var allowed = json.GetProperty("allowedTools").EnumerateArray().Select(x => x.GetString()).ToArray();
                allowed.ShouldContain("search_memory");
            }

            // The second LLM request should expose only the allowed tool(s)
            MockProvider.CapturedRequests.Count.ShouldBe(3);
            var second = MockProvider.CapturedRequests[1];
            second.Functions.ShouldNotBeNull();
            second.Functions!.Count.ShouldBe(1);
            second.Functions[0].Name.ShouldBe("search_memory");
        }
        finally
        {
            CleanupDir(root);
        }
    }
}

/// <summary>
/// Uses a real <see cref="AevatarToolManager"/> instead of the default MockToolManager from <see cref="AITestFixture"/>.
/// </summary>
public sealed class AgentSkillsToolFixture : AITestFixture
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        // Override the default MockToolManager registration: we want to run real tool implementations here.
        services.AddTransient<IAevatarToolManager>(sp =>
            new AevatarToolManager(sp.GetRequiredService<ILogger<AevatarToolManager>>()));
    }
}


