using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Configuration;
using Xunit;

namespace Aevatar.Agents.AI.Tests.Configuration;

public class AgentYamlConfigLoaderTests
{
    private readonly AgentYamlConfigLoader _loader = new();

    [Fact]
    public void LoadFromString_WithMinimalConfig_ShouldParseSuccessfully()
    {
        const string yaml = """
            id: "test-agent"
            name: "Test Agent"
            """;

        var config = _loader.LoadFromString(yaml);

        Assert.Equal("test-agent", config.Id);
        Assert.Equal("Test Agent", config.Name);
        Assert.Equal("1.0", config.Version);
    }

    [Fact]
    public void LoadFromString_WithFullConfig_ShouldParseAllFields()
    {
        const string yaml = """
            id: "coder"
            name: "Coder Agent"
            version: "2.0"
            
            persona:
              role: "Software Engineer"
              expertise:
                - "coding"
                - "debugging"
              style: "professional"
              traits:
                - "efficient"
                - "thorough"
            
            provider: "openai"
            model: "gpt-4"
            temperature: 0.3
            max_tokens: 8192
            top_p: 0.95
            frequency_penalty: 0.1
            presence_penalty: 0.2
            stop_sequences:
              - "END"
              - "STOP"
            
            tools:
              - file_read
              - file_write
              - bash
            
            system_prompt: |
              You are a helpful coding assistant.
              Follow best practices.
            
            capabilities:
              max_tool_calls_per_turn: 5
              max_history_length: 100
              can_spawn_children: true
              supports_streaming: false
              operation_timeout_seconds: 60
              record_tool_executions: false
            """;

        var config = _loader.LoadFromString(yaml);

        Assert.Equal("coder", config.Id);
        Assert.Equal("Coder Agent", config.Name);
        Assert.Equal("2.0", config.Version);

        Assert.NotNull(config.Persona);
        Assert.Equal("Software Engineer", config.Persona.Role);
        Assert.Equal(2, config.Persona.Expertise.Count);
        Assert.Contains("coding", config.Persona.Expertise);
        Assert.Equal("professional", config.Persona.Style);
        Assert.Equal(2, config.Persona.Traits.Count);

        Assert.Equal("openai", config.Provider);
        Assert.Equal("gpt-4", config.Model);
        Assert.Equal(0.3, config.Temperature);
        Assert.Equal(8192, config.MaxTokens);
        Assert.Equal(0.95, config.TopP);
        Assert.Equal(0.1, config.FrequencyPenalty);
        Assert.Equal(0.2, config.PresencePenalty);
        Assert.Equal(2, config.StopSequences.Count);

        Assert.Equal(3, config.Tools.Count);
        Assert.Contains("file_read", config.Tools);

        Assert.NotNull(config.SystemPrompt);
        Assert.Contains("helpful coding assistant", config.SystemPrompt);

        Assert.NotNull(config.Capabilities);
        Assert.Equal(5, config.Capabilities.MaxToolCallsPerTurn);
        Assert.Equal(100, config.Capabilities.MaxHistoryLength);
        Assert.True(config.Capabilities.CanSpawnChildren);
        Assert.False(config.Capabilities.SupportsStreaming);
        Assert.Equal(60, config.Capabilities.OperationTimeoutSeconds);
        Assert.False(config.Capabilities.RecordToolExecutions);
    }

    [Fact]
    public void LoadFromString_WithDefaultValues_ShouldApplyDefaults()
    {
        const string yaml = """
            id: "minimal"
            name: "Minimal Agent"
            provider: "claude"
            """;

        var config = _loader.LoadFromString(yaml);

        Assert.Equal("claude", config.Provider);
        Assert.Null(config.Model);
        Assert.Null(config.Temperature);
        Assert.Null(config.MaxTokens);
        Assert.Null(config.TopP);
    }

    [Fact]
    public void LoadFromString_WithEmptyYaml_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _loader.LoadFromString(""));
        Assert.Throws<ArgumentException>(() => _loader.LoadFromString("   "));
    }

    [Fact]
    public void LoadFromString_WithMissingId_ShouldThrowInvalidOperationException()
    {
        const string yaml = """
            name: "Agent without ID"
            """;

        Assert.Throws<InvalidOperationException>(() => _loader.LoadFromString(yaml));
    }

    [Fact]
    public void LoadFromString_WithOnlyId_ShouldUseIdAsName()
    {
        const string yaml = """
            id: "my-agent"
            """;

        var config = _loader.LoadFromString(yaml);

        Assert.Equal("my-agent", config.Id);
        Assert.Equal("my-agent", config.Name);
    }

    [Fact]
    public void LoadFromFile_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => 
            _loader.LoadFromFile("/non/existent/path/agent.yaml"));
    }

    [Fact]
    public void TryLoadFromFile_WithNonExistentFile_ShouldReturnNull()
    {
        var config = _loader.TryLoadFromFile("/non/existent/path/agent.yaml");
        Assert.Null(config);
    }

    [Fact]
    public void GetDefaultConfigDirectory_ShouldReturnUserProfilePath()
    {
        var path = AgentYamlConfigLoader.GetDefaultConfigDirectory();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".aevatar", "agents"), path);
    }

    [Fact]
    public void GetConfigFilePath_ShouldReturnCorrectPath()
    {
        var path = AgentYamlConfigLoader.GetConfigFilePath("coder");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".aevatar", "agents", "coder.yaml"), path);
    }

    [Fact]
    public void LoadFromString_WithExtensions_ShouldParseExtensions()
    {
        const string yaml = """
            id: "custom"
            name: "Custom Agent"
            extensions:
              custom_key: "custom_value"
              another_key: "another_value"
            """;

        var config = _loader.LoadFromString(yaml);

        Assert.NotNull(config.Extensions);
        Assert.Equal(2, config.Extensions.Count);
        Assert.Equal("custom_value", config.Extensions["custom_key"]);
    }

    [Fact]
    public void LoadFromString_WithUnknownFields_ShouldIgnoreUnknownFields()
    {
        const string yaml = """
            id: "test"
            name: "Test"
            unknown_field: "should be ignored"
            another_unknown:
              nested: true
            """;

        var config = _loader.LoadFromString(yaml);

        Assert.Equal("test", config.Id);
        Assert.Equal("Test", config.Name);
    }
}
