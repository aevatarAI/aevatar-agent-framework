using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Configuration;

public abstract class YamlConfigurableAIGAgentBase<TCustomState> : AIGAgentBase<TCustomState>
    where TCustomState : class, IMessage<TCustomState>, new()
{
    private AgentYamlConfig? _yamlConfig;
    private readonly AgentYamlConfigLoader _configLoader;

    protected YamlConfigurableAIGAgentBase()
    {
        _configLoader = new AgentYamlConfigLoader();
    }

    protected YamlConfigurableAIGAgentBase(string id) : base(id)
    {
        _configLoader = new AgentYamlConfigLoader();
    }

    protected AgentYamlConfig? YamlConfig => _yamlConfig;

    protected virtual string? YamlConfigPath => null;

    protected virtual string? YamlConfigContent => null;

    public override string SystemPrompt
    {
        get => _yamlConfig?.SystemPrompt ?? base.SystemPrompt;
        set => base.SystemPrompt = value;
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        using var initScope = StateProtectionContext.BeginInitializationScope();

        await LoadYamlConfigAsync(ct);

        await base.OnActivateAsync(ct);

        ApplyYamlConfigToAgent();
    }

    private async Task LoadYamlConfigAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(YamlConfigContent))
        {
            _yamlConfig = _configLoader.LoadFromString(YamlConfigContent);
            Logger.LogDebug("Loaded YAML config from embedded content for agent {AgentId}", Id);
            return;
        }

        if (!string.IsNullOrEmpty(YamlConfigPath))
        {
            _yamlConfig = await _configLoader.LoadFromFileAsync(YamlConfigPath, ct);
            Logger.LogDebug("Loaded YAML config from {Path} for agent {AgentId}", YamlConfigPath, Id);
            return;
        }

        var agentId = GetAgentIdForConfig();
        if (!string.IsNullOrEmpty(agentId))
        {
            var defaultPath = AgentYamlConfigLoader.GetConfigFilePath(agentId);
            _yamlConfig = _configLoader.TryLoadFromFile(defaultPath);
            if (_yamlConfig != null)
            {
                Logger.LogDebug("Loaded YAML config from default path {Path} for agent {AgentId}", defaultPath, Id);
            }
        }
    }

    protected virtual string? GetAgentIdForConfig()
    {
        var typeName = GetType().Name;
        if (typeName.EndsWith("Agent", StringComparison.OrdinalIgnoreCase))
            return typeName[..^5].ToLowerInvariant();
        if (typeName.EndsWith("GAgent", StringComparison.OrdinalIgnoreCase))
            return typeName[..^6].ToLowerInvariant();
        return typeName.ToLowerInvariant();
    }

    private void ApplyYamlConfigToAgent()
    {
        if (_yamlConfig == null) return;

        // Apply flat model properties (only if explicitly set)
        if (!string.IsNullOrEmpty(_yamlConfig.Model))
            Config.Model = _yamlConfig.Model;

        if (_yamlConfig.Temperature.HasValue)
            Config.Temperature = (float)_yamlConfig.Temperature.Value;

        if (_yamlConfig.MaxTokens.HasValue)
            Config.MaxOutputTokens = _yamlConfig.MaxTokens.Value;

        if (_yamlConfig.TopP.HasValue)
            Config.TopP = (float)_yamlConfig.TopP.Value;

        if (_yamlConfig.FrequencyPenalty.HasValue)
            Config.FrequencyPenalty = (float)_yamlConfig.FrequencyPenalty.Value;

        if (_yamlConfig.PresencePenalty.HasValue)
            Config.PresencePenalty = (float)_yamlConfig.PresencePenalty.Value;

        if (_yamlConfig.StopSequences.Count > 0)
        {
            Config.StopSequences.Clear();
            foreach (var seq in _yamlConfig.StopSequences)
                Config.StopSequences.Add(seq);
        }

        if (!string.IsNullOrEmpty(_yamlConfig.SystemPrompt))
        {
            Config.SystemPrompt = _yamlConfig.SystemPrompt;
        }

        OnYamlConfigApplied(_yamlConfig);
    }

    protected virtual void OnYamlConfigApplied(AgentYamlConfig config)
    {
    }

    protected string BuildSystemPromptFromPersona()
    {
        if (_yamlConfig?.Persona == null)
            return _yamlConfig?.SystemPrompt ?? base.SystemPrompt;

        var persona = _yamlConfig.Persona;
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(persona.Role))
            parts.Add($"You are {persona.Role}.");

        if (persona.Expertise.Count > 0)
            parts.Add($"Your expertise includes: {string.Join(", ", persona.Expertise)}.");

        if (!string.IsNullOrEmpty(persona.Style))
            parts.Add($"Communication style: {persona.Style}.");

        if (persona.Traits.Count > 0)
            parts.Add($"Key traits: {string.Join(", ", persona.Traits)}.");

        if (!string.IsNullOrEmpty(_yamlConfig.SystemPrompt))
            parts.Add(_yamlConfig.SystemPrompt);

        return string.Join("\n\n", parts);
    }
}
