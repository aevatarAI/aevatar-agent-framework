using System.Collections.Concurrent;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Messages;
using WorkflowDefinition = Aevatar.Agents.Cognitive.Primitives.WorkflowDefinition;
using Aevatar.Agents.Cognitive.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions.Runtime;

public sealed record SessionWorkflowRunRequest(
    string? WorkflowName,
    string? Message,
    string? Mode,
    Dictionary<string, object?>? Variables,
    string? RequestId);

public sealed record SessionWorkflowRunContext(
    string SessionId,
    string WorkflowName,
    string WorkflowPath,
    string CoordinatorActorId,
    RoleAIGAgent Coordinator,
    WorkflowDefinition Workflow);

public sealed class SessionWorkflowRunner
{
    private readonly IGAgentActorManager _actorManager;
    private readonly AgentBootstrapper _bootstrapper;
    private readonly IOptionsMonitor<LLMProvidersConfig> _llmProviders;
    private readonly IOptionsMonitor<SessionRuntimeOptions> _options;
    private readonly ILogger<SessionWorkflowRunner> _logger;
    private readonly WorkflowParser _workflowParser = new();

    private readonly ConcurrentDictionary<string, bool> _initializedCoordinators = new(StringComparer.Ordinal);
    private static int _workflowDagLogCount;

    public SessionWorkflowRunner(
        IGAgentActorManager actorManager,
        AgentBootstrapper bootstrapper,
        IOptionsMonitor<LLMProvidersConfig> llmProviders,
        IOptionsMonitor<SessionRuntimeOptions> options,
        ILogger<SessionWorkflowRunner> logger)
    {
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _llmProviders = llmProviders ?? throw new ArgumentNullException(nameof(llmProviders));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SessionWorkflowRunContext> PrepareAsync(
        SessionState state,
        SessionWorkflowRunRequest request,
        bool memoryEnabled,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);

        var workflow = LoadWorkflowDefinition(state);
        var workflowName = ResolveWorkflowName(state, workflow, request.WorkflowName);
        var coordinator = await EnsureCoordinatorAsync(state.SessionId, workflowName, memoryEnabled, ct);

        if (!string.Equals(workflow.Name, workflowName, StringComparison.OrdinalIgnoreCase))
        {
            workflow.Name = workflowName;
        }

        return new SessionWorkflowRunContext(
            SessionId: state.SessionId,
            WorkflowName: workflowName,
            WorkflowPath: (state.WorkflowPath ?? string.Empty).Trim(),
            CoordinatorActorId: coordinator.Id,
            Coordinator: coordinator,
            Workflow: workflow);
    }

    public Task ExecuteAsync(SessionWorkflowRunContext context, SessionWorkflowRunRequest request, CancellationToken ct)
    {
        var hasDagSteps = context.Workflow.Steps.Any(step =>
            step.Id is "dag_builder" or "maker_consensus_parse" or "verifier_quorum");
        if (Interlocked.Increment(ref _workflowDagLogCount) <= 3)
        {
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId = context.SessionId,
                    runId = request.RequestId ?? string.Empty,
                    hypothesisId = "H56",
                    location = "SessionWorkflowRunner.cs:ExecuteAsync",
                    message = "workflow_dag_steps_presence",
                    data = new
                    {
                        workflowName = context.WorkflowName,
                        stepsCount = context.Workflow.Steps.Count,
                        hasDagSteps
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }
        var variables = BuildVariables(context.Workflow, request, context.WorkflowName, context.SessionId, context.WorkflowPath);
        var evt = new StartWorkflowRequestEvent
        {
            WorkflowName = context.WorkflowName
        };
        foreach (var (key, value) in variables)
        {
            evt.Variables[key] = ProtoValueConverter.ToProto(value);
        }

        return PublishStartEventAsync(context.CoordinatorActorId, evt, ct);
    }

    private async Task PublishStartEventAsync(string coordinatorActorId, StartWorkflowRequestEvent evt, CancellationToken ct)
    {
        var actor = await _actorManager.GetActorAsync(coordinatorActorId);
        if (actor == null)
            throw new InvalidOperationException("Coordinator actor not available.");

        await actor.PublishEventAsync(evt, EventDirection.Self, ct, isInternalCall: false);
    }

    private WorkflowDefinition LoadWorkflowDefinition(SessionState state)
    {
        var path = (state.WorkflowPath ?? string.Empty).Trim();
        if (path.Length == 0)
            throw new InvalidOperationException("workflow_path is required to run workflow.");

        try
        {
            var yaml = File.ReadAllText(path);
            var workflow = _workflowParser.Parse(yaml);
            if (workflow.Steps.Count == 0)
                throw new InvalidOperationException("workflow steps are empty.");
            return workflow;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse workflow at '{path}': {ex.Message}", ex);
        }
    }

    private string ResolveWorkflowName(SessionState state, WorkflowDefinition workflow, string? requested)
    {
        var explicitName = (requested ?? string.Empty).Trim();
        if (explicitName.Length > 0)
        {
            if (!string.Equals(explicitName, workflow.Name, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("[SessionWorkflowRunner] Workflow name override: {Requested} (yaml name={YamlName})",
                    explicitName, workflow.Name);
            }

            return explicitName;
        }

        var stateName = (state.WorkflowName ?? string.Empty).Trim();
        if (stateName.Length > 0)
            return stateName;

        return string.IsNullOrWhiteSpace(workflow.Name) ? "workflow" : workflow.Name;
    }

    private async Task<RoleAIGAgent> EnsureCoordinatorAsync(
        string sessionId,
        string workflowName,
        bool memoryEnabled,
        CancellationToken ct)
    {
        var rawId = DeterministicGuid.FromString($"session:{sessionId}:coordinator:{workflowName}").ToString("D");
        var actorId = AgentId.Normalize<RoleAIGAgent>(rawId);

        var actor = await _actorManager.GetActorAsync(actorId)
                    ?? await _actorManager.CreateAndRegisterAsync<RoleAIGAgent>(rawId, ct);

        var coordinator = actor.GetAgent() as RoleAIGAgent
                          ?? throw new InvalidOperationException("Coordinator agent not available.");

        coordinator.ConfigureSessionContext(sessionId, memoryEnabled, memoryEnabled);

        coordinator.EnableChatHistoryInState = true;
        coordinator.EnableChatHistoryCompaction = false;
        coordinator.ChatHistoryMaxMessages = Math.Max(1, _options.CurrentValue.MaxSnapshotMessages);

        var role = (_options.CurrentValue.CoordinatorRole ?? string.Empty).Trim();
        if (role.Length == 0)
            role = "workflow_coordinator";

        if (_initializedCoordinators.TryAdd(actorId, true))
        {
            var providerName = ResolveProviderName(_llmProviders.CurrentValue);
            if (string.IsNullOrWhiteSpace(providerName))
                throw new InvalidOperationException("LLMProviders.default 未配置");

            await coordinator.InitializeAsync(providerName, config =>
            {
                var opts = _options.CurrentValue ?? new SessionRuntimeOptions();
                config.Temperature = (float)opts.Temperature;
                config.MaxOutputTokens = opts.MaxOutputTokens;
            }, ct);
        }

        await _bootstrapper.TryConfigureRoleAgentAsync(actor, role, ct);

        return coordinator;
    }

    private static string ResolveProviderName(LLMProvidersConfig config)
    {
        var fallback = config.Providers.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? string.Empty;

        return string.IsNullOrWhiteSpace(config.Default)
            ? fallback.Trim()
            : config.Default.Trim();
    }

    private static Dictionary<string, object> BuildVariables(
        WorkflowDefinition workflow,
        SessionWorkflowRunRequest request,
        string workflowName,
        string sessionId,
        string workflowPath)
    {
        var variables = NormalizeVariables(request.Variables);

        var inputNames = new HashSet<string>(
            workflow.Inputs.Select(i => i.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Mode) && inputNames.Contains("mode"))
        {
            variables["mode"] = request.Mode!.Trim();
        }

        var messageKey = ResolveMessageKey(inputNames);
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            if (messageKey != null && !variables.ContainsKey(messageKey))
            {
                variables[messageKey] = request.Message!.Trim();
            }
        }

        if (!variables.ContainsKey("workflow_name"))
        {
            if (inputNames.Contains("workflow_name"))
                variables["workflow_name"] = workflowName;
        }

        if (!variables.ContainsKey("workflow_name"))
            variables["workflow_name"] = workflowName;

        if (inputNames.Contains("session_id") && !variables.ContainsKey("session_id"))
        {
            variables["session_id"] = sessionId;
        }

        if (!string.IsNullOrWhiteSpace(workflowPath) && !variables.ContainsKey("workflow_path"))
        {
            variables["workflow_path"] = workflowPath;
        }

        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            if (inputNames.Contains("request_id") && !variables.ContainsKey("request_id"))
            {
                variables["request_id"] = request.RequestId!.Trim();
            }
            else if (inputNames.Contains("run_id") && !variables.ContainsKey("run_id"))
            {
                variables["run_id"] = request.RequestId!.Trim();
            }
        }

        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            JsonSerializer.Serialize(new
            {
                sessionId,
                runId = request.RequestId ?? string.Empty,
                hypothesisId = "H2",
                location = "SessionWorkflowRunner.cs:BuildVariables",
                message = "variables_built",
                data = new
                {
                    workflowName,
                    inputNames,
                    variablesKeys = variables.Keys,
                    messageKey,
                    hasAttachmentsInput = inputNames.Contains("attachments"),
                    hasAttachmentsVar = variables.ContainsKey("attachments")
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        return variables;
    }

    private static string? ResolveMessageKey(IReadOnlySet<string> inputNames)
    {
        if (inputNames.Contains("question")) return "question";
        if (inputNames.Contains("task")) return "task";
        if (inputNames.Contains("input")) return "input";
        if (inputNames.Contains("message")) return "message";
        return null;
    }

    private static Dictionary<string, object> NormalizeVariables(Dictionary<string, object?>? variables)
    {
        if (variables == null || variables.Count == 0)
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, object>(variables.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in variables)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key] = NormalizeValue(value);
        }

        return result;
    }

    private static object NormalizeValue(object? value)
    {
        if (value is null)
            return string.Empty;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.TryGetInt64(out var i64)
                    ? i64
                    : element.TryGetDouble(out var dbl)
                        ? dbl
                        : element.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => element.EnumerateArray().Select(v => NormalizeValue(v)).ToList(),
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(p => p.Name, p => NormalizeValue(p.Value), StringComparer.OrdinalIgnoreCase),
                _ => element.GetRawText()
            };
        }

        return value;
    }
}
