namespace Aevatar.Agents.AI.Abstractions.Configuration;

public sealed record AgentYamlConfig
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Version { get; init; } = "1.0";

    public string Provider { get; init; } = "default";

    public string? Model { get; init; }

    public double? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public double? TopP { get; init; }

    public double? FrequencyPenalty { get; init; }

    public double? PresencePenalty { get; init; }

    public IReadOnlyList<string> StopSequences { get; init; } = [];

    public AgentPersonaConfig? Persona { get; init; }

    public IReadOnlyList<string> Tools { get; init; } = [];

    public IReadOnlyList<string> Skills { get; init; } = [];

    public string? SystemPrompt { get; init; }

    public AgentCapabilitiesConfig? Capabilities { get; init; }

    public IReadOnlyDictionary<string, object>? Extensions { get; init; }
}

public sealed record AgentPersonaConfig
{
    public string? Role { get; init; }

    public IReadOnlyList<string> Expertise { get; init; } = [];

    public string? Style { get; init; }

    public IReadOnlyList<string> Traits { get; init; } = [];
}

public sealed record AgentCapabilitiesConfig
{
    public int MaxToolCallsPerTurn { get; init; } = 10;

    public int MaxHistoryLength { get; init; } = 50;

    public bool CanSpawnChildren { get; init; } = false;

    public bool SupportsStreaming { get; init; } = true;

    public int OperationTimeoutSeconds { get; init; } = 120;

    public bool RecordToolExecutions { get; init; } = true;
}
