using System.Collections.Concurrent;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tool.Evolution;

// ============================================================
//  ToolEvolutionRegistry
//
//  中文 + ASCII:
//  - Replace/Canary 两种注册策略。
//  - Canary 通过 metrics 触发回滚（best-effort）。
// ============================================================
/// <summary>
/// In-memory tool evolution registry (best-effort).
/// </summary>
public sealed class ToolEvolutionRegistry : IToolEvolutionRegistry
{
    private readonly IAevatarToolManager _toolManager;
    private readonly ToolMetricsStore _metricsStore;
    private readonly ToolEvolutionOptions _options;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, ToolEvolutionState> _states = new(StringComparer.OrdinalIgnoreCase);

    public ToolEvolutionRegistry(
        IAevatarToolManager toolManager,
        ToolMetricsStore metricsStore,
        ToolEvolutionOptions options,
        ILogger? logger = null)
    {
        _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        _metricsStore = metricsStore ?? throw new ArgumentNullException(nameof(metricsStore));
        _options = options ?? new ToolEvolutionOptions();
        _logger = logger;
    }

    public void RegisterBaseTool(ToolDefinition tool)
    {
        if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
            return;

        var state = _states.GetOrAdd(tool.Name, _ => new ToolEvolutionState());
        state.BaseTool = tool;
        state.ActiveTool ??= tool;
    }

    public async Task RegisterEvolvedToolAsync(
        ToolDefinition tool,
        ToolEvolutionPolicy? policy,
        CancellationToken cancellationToken = default)
    {
        if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
            return;

        var effectivePolicy = policy ?? _options.DefaultPolicy ?? new ToolEvolutionPolicy();
        var state = _states.GetOrAdd(tool.Name, _ => new ToolEvolutionState());
        state.BaseTool ??= tool;

        if (effectivePolicy.Strategy == ToolEvolutionStrategy.Replace)
        {
            state.PreviousTool = state.ActiveTool;
            state.ActiveTool = tool;
            await _toolManager.RegisterToolAsync(tool, cancellationToken);
            return;
        }

        state.Candidates.Add(new ToolEvolutionCandidate(tool, effectivePolicy));
    }

    public ToolDefinition? ResolveToolForExecution(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        if (!_states.TryGetValue(toolName, out var state))
            return null;

        var candidate = SelectCanaryCandidate(state);
        if (candidate != null)
            return candidate.Tool;

        return state.ActiveTool ?? state.BaseTool;
    }

    public IReadOnlyList<ToolDefinition> GetActiveTools()
    {
        var list = new List<ToolDefinition>();
        foreach (var state in _states.Values)
        {
            var tool = state.ActiveTool ?? state.BaseTool;
            if (tool != null)
                list.Add(tool);
        }

        return list;
    }

    private ToolEvolutionCandidate? SelectCanaryCandidate(ToolEvolutionState state)
    {
        if (state.Candidates.Count == 0)
            return null;

        foreach (var candidate in state.Candidates)
        {
            if (candidate.Disabled)
                continue;

            if (ShouldRollback(candidate))
            {
                candidate.Disabled = true;
                _logger?.LogWarning(
                    "Tool evolution rollback: {Tool}@{Version}",
                    candidate.Tool.Name, candidate.Tool.Version);
                continue;
            }

            var pct = Math.Clamp(candidate.Policy.CanaryPercentage, 0, 1);
            if (pct <= 0)
                continue;

            if (Random.Shared.NextDouble() < pct)
                return candidate;
        }

        return null;
    }

    private bool ShouldRollback(ToolEvolutionCandidate candidate)
    {
        var entry = _metricsStore.GetEntry(candidate.Tool.Name, candidate.Tool.Version);
        if (entry == null)
            return false;

        if (entry.TotalCalls < candidate.Policy.MinCallsBeforeRollback)
            return false;

        return entry.SuccessRate < 1 - candidate.Policy.RollbackFailureRate;
    }

    private sealed class ToolEvolutionState
    {
        public ToolDefinition? BaseTool { get; set; }
        public ToolDefinition? ActiveTool { get; set; }
        public ToolDefinition? PreviousTool { get; set; }
        public List<ToolEvolutionCandidate> Candidates { get; } = new();
    }

    private sealed class ToolEvolutionCandidate
    {
        public ToolEvolutionCandidate(ToolDefinition tool, ToolEvolutionPolicy policy)
        {
            Tool = tool;
            Policy = policy;
        }

        public ToolDefinition Tool { get; }
        public ToolEvolutionPolicy Policy { get; }
        public bool Disabled { get; set; }
    }
}
