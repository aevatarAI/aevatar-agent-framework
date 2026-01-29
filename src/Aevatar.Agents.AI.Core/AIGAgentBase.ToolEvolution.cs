using System.Text.Json;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Evolution;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.AI.Tool.Tools;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    // ============================================================
    //  Tool Evolution (opt-in)
    //
    //  中文 + ASCII:
    //  - 仅在显式启用时注入 hooks/registry。
    //  - best-effort，不影响主执行链路。
    // ============================================================

    private ToolEvolutionOptions _toolEvolutionOptions = new();
    private ToolMetricsStore? _toolMetricsStore;
    private ToolEvolutionRegistry? _toolEvolutionRegistry;

    /// <summary>
    /// Tool evolution options (opt-in).
    /// </summary>
    public ToolEvolutionOptions ToolEvolutionOptions
    {
        get => _toolEvolutionOptions;
        set
        {
            _toolEvolutionOptions = value ?? new ToolEvolutionOptions();
            _hookPipeline = null;
        }
    }

    internal void InjectToolEvolutionOptions(ToolEvolutionOptions? options)
    {
        if (options == null)
            return;

        ToolEvolutionOptions = options;
        _toolEvolutionRegistry = null;
        _toolMetricsStore = null;
        _hookPipeline = null;

        if (!ToolEvolutionOptions.Enabled)
            return;

        EnsureToolManagerInitialized();
        if (ToolManager is AevatarToolManager manager)
        {
            var registry = CreateToolEvolutionRegistry(manager);
            manager.EvolutionRegistry = registry;
            _toolEvolutionRegistry = registry;
        }
    }

    protected ToolMetricsStore ToolMetricsStore
        => _toolMetricsStore ??= new ToolMetricsStore();

    protected ToolEvolutionRegistry? ToolEvolutionRegistry
        => _toolEvolutionRegistry;

    private ToolEvolutionRegistry? CreateToolEvolutionRegistry(IAevatarToolManager toolManager)
    {
        if (!ToolEvolutionOptions.Enabled)
            return null;

        return new ToolEvolutionRegistry(
            toolManager,
            ToolMetricsStore,
            ToolEvolutionOptions,
            Logger);
    }

    private async Task AppendToolFeedbackMemoryAsync(
        ToolExecutionFeedback feedback,
        CancellationToken ct)
    {
        if (MemoryStore == null)
            return;

        try
        {
            var scope = new MemoryScope
            {
                Type = MemoryScopeType.PrivateAgent,
                ScopeId = Id.ToString()
            };
            var memoryId = BuildMemoryId(scope);
            var entry = new MemoryEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                MemoryId = memoryId,
                Scope = scope,
                AgentId = Id.ToString(),
                Role = "tool",
                Content = JsonSerializer.Serialize(new
                {
                    tool_name = feedback.ToolName,
                    tool_version = feedback.ToolVersion,
                    tool_call_id = feedback.ToolCallId,
                    success = feedback.Success,
                    error_message = feedback.ErrorMessage,
                    duration_ms = feedback.DurationMs
                }),
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            entry.Tags["tool_name"] = feedback.ToolName ?? string.Empty;
            entry.Tags["tool_version"] = feedback.ToolVersion ?? string.Empty;
            entry.Tags["success"] = feedback.Success ? "true" : "false";
            if (!string.IsNullOrWhiteSpace(feedback.ErrorCode))
                entry.Tags["error_code"] = feedback.ErrorCode;

            await MemoryStore.AppendAsync(entry, ct);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "AppendToolFeedbackMemoryAsync failed (best-effort).");
        }
    }
}
