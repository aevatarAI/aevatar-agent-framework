using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Agents.AI.Core.Configuration;

public sealed class AgentYamlConfigLoader
{
    private readonly ILogger<AgentYamlConfigLoader>? _logger;
    private readonly IDeserializer _deserializer;

    public AgentYamlConfigLoader(ILogger<AgentYamlConfigLoader>? logger = null)
    {
        _logger = logger;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public AgentYamlConfig LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Agent YAML config not found: {filePath}", filePath);

        var yaml = File.ReadAllText(filePath);
        return LoadFromString(yaml);
    }

    public AgentYamlConfig LoadFromString(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new ArgumentException("YAML content cannot be empty", nameof(yaml));

        var raw = _deserializer.Deserialize<RawAgentConfig>(yaml);
        return MapToAgentYamlConfig(raw);
    }

    public async Task<AgentYamlConfig> LoadFromFileAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Agent YAML config not found: {filePath}", filePath);

        var yaml = await File.ReadAllTextAsync(filePath, ct);
        return LoadFromString(yaml);
    }

    public AgentYamlConfig? TryLoadFromFile(string filePath)
    {
        try
        {
            return LoadFromFile(filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load agent config from {FilePath}", filePath);
            return null;
        }
    }

    public static string GetDefaultConfigDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar", "agents");
    }

    public static string GetSkillsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar", "skills");
    }

    public static string GetConfigFilePath(string agentId)
    {
        return Path.Combine(GetDefaultConfigDirectory(), $"{agentId}.yaml");
    }

    public static string GetSkillFilePath(string skillName)
    {
        return Path.Combine(GetSkillsDirectory(), $"{skillName}.md");
    }

    private static AgentYamlConfig MapToAgentYamlConfig(RawAgentConfig raw)
    {
        return new AgentYamlConfig
        {
            Id = raw.Id ?? throw new InvalidOperationException("Agent 'id' is required"),
            Name = raw.Name ?? raw.Id,
            Version = raw.Version ?? "1.0",
            Provider = raw.Provider ?? "default",
            Model = raw.Model,
            Temperature = raw.Temperature,
            MaxTokens = raw.MaxTokens,
            TopP = raw.TopP,
            FrequencyPenalty = raw.FrequencyPenalty,
            PresencePenalty = raw.PresencePenalty,
            StopSequences = raw.StopSequences ?? [],
            Persona = MapPersona(raw.Persona),
            Tools = raw.Tools ?? [],
            Skills = raw.Skills ?? [],
            SystemPrompt = raw.SystemPrompt,
            Capabilities = MapCapabilities(raw.Capabilities),
            Extensions = raw.Extensions?.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)kvp.Value)
        };
    }

    private static AgentPersonaConfig? MapPersona(RawPersonaConfig? raw)
    {
        if (raw == null) return null;

        return new AgentPersonaConfig
        {
            Role = raw.Role,
            Expertise = raw.Expertise ?? [],
            Style = raw.Style,
            Traits = raw.Traits ?? []
        };
    }

    private static AgentCapabilitiesConfig? MapCapabilities(RawCapabilitiesConfig? raw)
    {
        if (raw == null) return null;

        return new AgentCapabilitiesConfig
        {
            MaxToolCallsPerTurn = raw.MaxToolCallsPerTurn ?? 10,
            MaxHistoryLength = raw.MaxHistoryLength ?? 50,
            CanSpawnChildren = raw.CanSpawnChildren ?? false,
            SupportsStreaming = raw.SupportsStreaming ?? true,
            OperationTimeoutSeconds = raw.OperationTimeoutSeconds ?? 120,
            RecordToolExecutions = raw.RecordToolExecutions ?? true
        };
    }

    private sealed class RawAgentConfig
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public double? TopP { get; set; }
        public double? FrequencyPenalty { get; set; }
        public double? PresencePenalty { get; set; }
        public List<string>? StopSequences { get; set; }
        public RawPersonaConfig? Persona { get; set; }
        public List<string>? Tools { get; set; }
        public List<string>? Skills { get; set; }
        public string? SystemPrompt { get; set; }
        public RawCapabilitiesConfig? Capabilities { get; set; }
        public Dictionary<string, string>? Extensions { get; set; }
    }

    private sealed class RawPersonaConfig
    {
        public string? Role { get; set; }
        public List<string>? Expertise { get; set; }
        public string? Style { get; set; }
        public List<string>? Traits { get; set; }
    }

    private sealed class RawCapabilitiesConfig
    {
        public int? MaxToolCallsPerTurn { get; set; }
        public int? MaxHistoryLength { get; set; }
        public bool? CanSpawnChildren { get; set; }
        public bool? SupportsStreaming { get; set; }
        public int? OperationTimeoutSeconds { get; set; }
        public bool? RecordToolExecutions { get; set; }
    }
}
