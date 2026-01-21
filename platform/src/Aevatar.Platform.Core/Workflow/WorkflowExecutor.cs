using System.IO;
using System.Text;
using System.Text.Json;

namespace Aevatar.Platform.Core.Workflow;

public sealed class WorkflowExecutor
{
    private readonly HermesRouter _hermes;
    private readonly RoleAgentRunner _runner;

    public WorkflowExecutor()
    {
        _runner = new RoleAgentRunner();
        _hermes = new HermesRouter(_runner);
    }

    public async Task<WorkflowRunResult> ExecuteAsync(
        WorkflowPlan plan,
        WorkflowRunInput input,
        CancellationToken ct)
    {
        if (ContainsHermesNode(plan))
        {
            var hermesResult = await _hermes.RouteAsync(input, ct);
            if (!hermesResult.Ok)
                return hermesResult;

            var selected = hermesResult.SelectedWorkflow ?? string.Empty;
            if (selected.Length == 0 || selected.Equals("hermes", StringComparison.OrdinalIgnoreCase))
                return hermesResult;

            if (TryParseAgentSelection(selected, out var role))
            {
                var result = await ExecuteSingleAgentAsync(role, input, ct);
                return result with
                {
                    SelectedWorkflow = $"agent:{role}",
                    CreatedAgentRoles = hermesResult.CreatedAgentRoles
                };
            }

            var downstream = await TryExecuteWorkflowByNameAsync(selected, input, ct);
            if (!downstream.Ok)
            {
                var note = $"{hermesResult.Note}\n\n(执行失败: {downstream.Note})";
                return hermesResult with { Note = note };
            }

            return downstream with
            {
                Note = $"{hermesResult.Note}\n\n{downstream.Note}",
                SelectedWorkflow = hermesResult.SelectedWorkflow,
                CreatedWorkflow = hermesResult.CreatedWorkflow,
                CreatedAgentRoles = hermesResult.CreatedAgentRoles
            };
        }

        return await ExecuteSingleRoleAsync(plan, input, ct);
    }

    public async Task<WorkflowRunResult> ExecuteStreamingAsync(
        WorkflowPlan plan,
        WorkflowRunInput input,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onDelta);

        if (ContainsHermesNode(plan))
        {
            var hermesResult = await _hermes.RouteStreamingAsync(input, onDelta, ct);
            #region agent log
            DebugLog(
                "WorkflowExecutor.cs:ExecuteStreamingAsync",
                "hermes_result",
                new
                {
                    ok = hermesResult.Ok,
                    selectedWorkflow = hermesResult.SelectedWorkflow ?? string.Empty,
                    createdWorkflow = hermesResult.CreatedWorkflow ?? string.Empty,
                    noteLen = hermesResult.Note?.Length ?? 0
                },
                input.ConfigDirectory,
                $"run_{Guid.NewGuid():N}",
                "H2");
            #endregion
            if (!hermesResult.Ok)
            {
                if (!string.IsNullOrWhiteSpace(hermesResult.Note))
                    await onDelta(hermesResult.Note, ct);
                return hermesResult;
            }

            var selected = hermesResult.SelectedWorkflow ?? string.Empty;
            if (selected.Length == 0 || selected.Equals("hermes", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(hermesResult.Note))
                    await onDelta(hermesResult.Note, ct);
                return hermesResult;
            }

            if (TryParseAgentSelection(selected, out var role))
            {
                var result = await ExecuteSingleAgentStreamAsync(role, input, onDelta, ct);
                return result with
                {
                    SelectedWorkflow = $"agent:{role}",
                    CreatedAgentRoles = hermesResult.CreatedAgentRoles
                };
            }

            if (!string.IsNullOrWhiteSpace(hermesResult.Note))
            {
                await onDelta(hermesResult.Note, ct);
                await onDelta("\n\n", ct);
            }

            var downstream = await TryExecuteWorkflowByNameStreamingAsync(selected, input, onDelta, ct);
            #region agent log
            DebugLog(
                "WorkflowExecutor.cs:ExecuteStreamingAsync",
                "downstream_result",
                new
                {
                    ok = downstream.Ok,
                    selectedWorkflow = downstream.SelectedWorkflow ?? string.Empty,
                    noteLen = downstream.Note?.Length ?? 0
                },
                input.ConfigDirectory,
                $"run_{Guid.NewGuid():N}",
                "H2");
            #endregion
            if (!downstream.Ok)
            {
                var failure = $"(执行失败: {downstream.Note})";
                await onDelta(failure, ct);
                var note = string.IsNullOrWhiteSpace(hermesResult.Note)
                    ? failure
                    : $"{hermesResult.Note}\n\n{failure}";
                return hermesResult with { Note = note };
            }

            return downstream with
            {
                Note = string.IsNullOrWhiteSpace(hermesResult.Note)
                    ? downstream.Note
                    : $"{hermesResult.Note}\n\n{downstream.Note}",
                SelectedWorkflow = hermesResult.SelectedWorkflow,
                CreatedWorkflow = hermesResult.CreatedWorkflow,
                CreatedAgentRoles = hermesResult.CreatedAgentRoles
            };
        }

        return await ExecuteSingleRoleStreamAsync(plan, input, onDelta, ct);
    }

    private static bool ContainsHermesNode(WorkflowPlan plan)
    {
        return plan.OrderedNodes.Any(n =>
            string.Equals(n.Type, "hermes", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<WorkflowRunResult> ExecuteSingleRoleAsync(
        WorkflowPlan plan,
        WorkflowRunInput input,
        CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        if (plan.OrderedNodes.Count == 0)
            return new WorkflowRunResult(runId, false, "workflow has no nodes");

        var role = plan.OrderedNodes[0].Type;
        var response = await _runner.RunAsync(
            role,
            input.UserMessage,
            input,
            _runner.BuildDefaultOptions(),
            ct);
        return new WorkflowRunResult(runId, true, response);
    }

    private async Task<WorkflowRunResult> ExecuteSingleRoleStreamAsync(
        WorkflowPlan plan,
        WorkflowRunInput input,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        if (plan.OrderedNodes.Count == 0)
            return new WorkflowRunResult(runId, false, "workflow has no nodes");

        var role = plan.OrderedNodes[0].Type;
        var builder = new StringBuilder();
        await foreach (var chunk in _runner.RunStreamAsync(
                           role,
                           input.UserMessage,
                           input,
                           _runner.BuildDefaultOptions(),
                           ct))
        {
            if (string.IsNullOrEmpty(chunk))
                continue;

            builder.Append(chunk);
            await onDelta(chunk, ct);
        }

        return new WorkflowRunResult(runId, true, builder.ToString());
    }

    private async Task<WorkflowRunResult> ExecuteSingleAgentAsync(
        string role,
        WorkflowRunInput input,
        CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        var response = await _runner.RunAsync(
            role,
            input.UserMessage,
            input,
            _runner.BuildDefaultOptions(),
            ct);
        return new WorkflowRunResult(runId, true, response, SelectedWorkflow: $"agent:{role}");
    }

    private async Task<WorkflowRunResult> ExecuteSingleAgentStreamAsync(
        string role,
        WorkflowRunInput input,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        var builder = new StringBuilder();
        await foreach (var chunk in _runner.RunStreamAsync(
                           role,
                           input.UserMessage,
                           input,
                           _runner.BuildDefaultOptions(),
                           ct))
        {
            if (string.IsNullOrEmpty(chunk))
                continue;
            builder.Append(chunk);
            await onDelta(chunk, ct);
        }

        return new WorkflowRunResult(runId, true, builder.ToString(), SelectedWorkflow: $"agent:{role}");
    }

    private async Task<WorkflowRunResult> TryExecuteWorkflowByNameAsync(
        string workflowName,
        WorkflowRunInput input,
        CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        var workflowFile = ResolveWorkflowFile(workflowName, input.ConfigDirectory);
        if (workflowFile == null)
            return new WorkflowRunResult(runId, false, $"workflow '{workflowName}' not found");

        try
        {
            var raw = await File.ReadAllTextAsync(workflowFile, ct);
            var compiler = new PlatformMeshCompiler(
                configAgentsDir: Path.Combine(input.ConfigDirectory, "agents"));
            var compile = compiler.Compile(raw);
            if (!compile.Ok || compile.Definition == null)
            {
                var msg = string.Join("; ", compile.Errors.Select(e => e.Code));
                #region agent log
                DebugLog(
                    "WorkflowExecutor.cs:TryExecuteWorkflowByNameAsync",
                    "workflow_compile_failed",
                    new
                    {
                        workflowName,
                        workflowFile,
                        errors = compile.Errors.Select(e => $"{e.Code}:{e.Path}").ToArray()
                    },
                    input.ConfigDirectory,
                    runId,
                    "H2");
                #endregion
                return new WorkflowRunResult(runId, false, $"workflow compile failed: {msg}");
            }

            var plan = new WorkflowEngine().Plan(compile.Definition);
            if (!plan.Ok || plan.Plan == null)
            {
                var msg = string.Join("; ", plan.Errors.Select(e => e.Code));
                #region agent log
                DebugLog(
                    "WorkflowExecutor.cs:TryExecuteWorkflowByNameAsync",
                    "workflow_plan_failed",
                    new
                    {
                        workflowName,
                        workflowFile,
                        errors = plan.Errors.Select(e => $"{e.Code}:{e.Path}").ToArray()
                    },
                    input.ConfigDirectory,
                    runId,
                    "H2");
                #endregion
                return new WorkflowRunResult(runId, false, $"workflow plan failed: {msg}");
            }

            var nextInput = input with { WorkflowName = workflowName };
            var result = await ExecuteSingleRoleAsync(plan.Plan, nextInput, ct);
            return result with { SelectedWorkflow = workflowName };
        }
        catch (Exception ex)
        {
            return new WorkflowRunResult(runId, false, $"workflow error: {ex.Message}");
        }
    }

    private async Task<WorkflowRunResult> TryExecuteWorkflowByNameStreamingAsync(
        string workflowName,
        WorkflowRunInput input,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        var workflowFile = ResolveWorkflowFile(workflowName, input.ConfigDirectory);
        if (workflowFile == null)
            return new WorkflowRunResult(runId, false, $"workflow '{workflowName}' not found");

        try
        {
            var raw = await File.ReadAllTextAsync(workflowFile, ct);
            var compiler = new PlatformMeshCompiler(
                configAgentsDir: Path.Combine(input.ConfigDirectory, "agents"));
            var compile = compiler.Compile(raw);
            if (!compile.Ok || compile.Definition == null)
            {
                var msg = string.Join("; ", compile.Errors.Select(e => e.Code));
                #region agent log
                DebugLog(
                    "WorkflowExecutor.cs:TryExecuteWorkflowByNameStreamingAsync",
                    "workflow_compile_failed",
                    new
                    {
                        workflowName,
                        workflowFile,
                        errors = compile.Errors.Select(e => $"{e.Code}:{e.Path}").ToArray()
                    },
                    input.ConfigDirectory,
                    runId,
                    "H2");
                #endregion
                return new WorkflowRunResult(runId, false, $"workflow compile failed: {msg}");
            }

            var plan = new WorkflowEngine().Plan(compile.Definition);
            if (!plan.Ok || plan.Plan == null)
            {
                var msg = string.Join("; ", plan.Errors.Select(e => e.Code));
                #region agent log
                DebugLog(
                    "WorkflowExecutor.cs:TryExecuteWorkflowByNameStreamingAsync",
                    "workflow_plan_failed",
                    new
                    {
                        workflowName,
                        workflowFile,
                        errors = plan.Errors.Select(e => $"{e.Code}:{e.Path}").ToArray()
                    },
                    input.ConfigDirectory,
                    runId,
                    "H2");
                #endregion
                return new WorkflowRunResult(runId, false, $"workflow plan failed: {msg}");
            }

            var nextInput = input with { WorkflowName = workflowName };
            var result = await ExecuteSingleRoleStreamAsync(plan.Plan, nextInput, onDelta, ct);
            return result with { SelectedWorkflow = workflowName };
        }
        catch (Exception ex)
        {
            return new WorkflowRunResult(runId, false, $"workflow error: {ex.Message}");
        }
    }

    private static string? ResolveWorkflowFile(string workflow, string configDir)
    {
        var name = (workflow ?? string.Empty).Trim();
        if (name.Length == 0)
            return null;

        if (File.Exists(name))
            return Path.GetFullPath(name);

        var dir = Path.Combine(configDir, "workflows");
        var json = Path.Combine(dir, $"{name}.json");
        if (File.Exists(json))
            return json;

        var yaml = Path.Combine(dir, $"{name}.yaml");
        if (File.Exists(yaml))
            return yaml;

        var yml = Path.Combine(dir, $"{name}.yml");
        if (File.Exists(yml))
            return yml;

        return null;
    }

    private static bool TryParseAgentSelection(string selected, out string role)
    {
        role = string.Empty;
        if (string.IsNullOrWhiteSpace(selected))
            return false;
        if (!selected.StartsWith("agent:", StringComparison.OrdinalIgnoreCase))
            return false;
        role = selected["agent:".Length..].Trim();
        return role.Length > 0;
    }

    private const string DebugLogPath = "/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log";

    private static void DebugLog(
        string location,
        string message,
        object data,
        string sessionId,
        string runId,
        string hypothesisId)
    {
        try
        {
            var payload = new
            {
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sessionId,
                runId,
                hypothesisId
            };
            var json = JsonSerializer.Serialize(payload);
            File.AppendAllText(DebugLogPath, json + Environment.NewLine);
        }
        catch
        {
            // best-effort only
        }
    }
}
