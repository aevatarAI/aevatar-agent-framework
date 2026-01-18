using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.Configuration;
using Aevatar.CognitiveMesh.Dsl.Validation;

namespace Aevatar.Platform.Core.Workflow;

public sealed class HermesRouter
{
    private readonly RoleAgentRunner _runner;
    private static readonly IReadOnlyList<string> WorkflowExtensions = new[] { ".json", ".yaml", ".yml" };
    private static readonly IReadOnlyList<string> AgentExtensions = new[] { ".yaml", ".yml" };

    public HermesRouter(RoleAgentRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<WorkflowRunResult> RouteAsync(WorkflowRunInput input, CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        try
        {
        var available = ListWorkflowNames(input.ConfigDirectory);
            var prompt = BuildHermesPrompt(input, available);
            var response = await _runner.RunAsync("hermes", prompt, input, _runner.BuildHermesOptions(), ct);

            if (!TryParseDecision(response, out var decision, out var error))
            {
                var parseNote = $"Hermes parse failed: {error}\n\nRaw:\n{response}";
                return new WorkflowRunResult(runId, false, parseNote);
            }

            var result = ApplyDecision(decision!, input);
            var header = string.IsNullOrWhiteSpace(decision!.Message)
                ? result.Note
                : decision.Message!.Trim();

            if (result.Ok && !string.IsNullOrWhiteSpace(result.CreatedWorkflow))
            {
                var check = await TryCompileWorkflowAsync(result.CreatedWorkflow!, input, ct);
                if (!check.Ok)
                {
                    var repair = await TryRepairWorkflowAsync(
                        result.CreatedWorkflow!,
                        check.Errors,
                        input,
                        ct);
                    if (!repair.Ok)
                    {
                        var note = string.IsNullOrWhiteSpace(header)
                            ? repair.Note
                            : $"{header}\n\n{repair.Note}";
                        return repair with { Note = note };
                    }

                    var merged = string.IsNullOrWhiteSpace(header)
                        ? repair.Note
                        : $"{header}\n\n{repair.Note}";
                    return repair with { Note = merged };
                }
            }

            return result with { Note = header };
        }
        catch (Exception ex)
        {
            return new WorkflowRunResult(runId, false, $"Hermes error: {ex.Message}");
        }
    }

    private static string BuildHermesPrompt(
        WorkflowRunInput input,
        IReadOnlyList<string> availableWorkflows)
    {
        var workflows = availableWorkflows.Count == 0
            ? "(none)"
            : string.Join(", ", availableWorkflows);

        var attached = input.AttachedFiles.Count == 0
            ? "(none)"
            : string.Join(", ", input.AttachedFiles);

        var roles = ListRoleNames(input);
        var roleText = roles.Count == 0 ? "(none)" : string.Join(", ", roles);

        return $$"""
You are Hermes, a workflow router.
Your task: select an existing workflow from ~/.aevatar/workflows or create a new workflow + roles to satisfy the user's intent.

Context:
- available_workflows: {{workflows}}
- available_roles: {{roleText}}
- working_directory: {{input.WorkingDirectory}}
- global_agents_dir: {{Path.Combine(input.ConfigDirectory, "agents")}}
- local_agents_dir: {{Path.Combine(input.WorkingDirectory, "aevatar", "agents")}}
- attached_files: {{attached}}

Tools:
- file_read: read UTF-8 text within allowed roots.
- file_write: write UTF-8 text within allowed roots.

Requirements:
- Use file_read to inspect existing workflows/agents when needed.
- Use file_write to create new agent/workflow files under ~/.aevatar/agents and ~/.aevatar/workflows when action=create.
- If selecting, use action=select and set selected_workflow.
- If creating, use action=create and provide workflow + optional agents.
- Workflow DSL v0.1 fields required: dsl_version, goal, strategy, budget, nodes, edges, constraints.
- goal must be an object: { name: string, success_metric: string? }.
- budget must be an object: { max_steps: int, token_limit: int }.
- strategy must be one of: cot/tot/got/uot_comb/uot_expl/uot_trans.
- node params must use "params" field (NOT "config").
- constraints must be list of objects: { type: string, value: any }.
- node.type must be one of: DivergentAgent, ConvergentAgent, WorkerAgent, CriticAgent, MetaAgent, or a role name (from YAML).
- Keep workflow minimal (1-3 nodes) unless clearly needed.
- Respond in Chinese in the "message" field.
- Output JSON ONLY. No markdown, no extra text.

Example minimal YAML:
dsl_version: "0.1"
goal:
  name: "greeting"
  success_metric: "user greeted"
strategy: "cot"
budget:
  max_steps: 3
  token_limit: 500
nodes:
  - id: "greeter"
    type: "WorkerAgent"
    params:
      role: "greeter"
edges: []
constraints: []

JSON schema:
{
  "action": "select" | "create",
  "selected_workflow": "name or null",
  "workflow": {
    "name": "workflow name",
    "format": "yaml" | "json",
    "content": "full file content"
  },
  "agents": [
    { "name": "role_name", "content": "yaml content" }
  ],
  "message": "short response to user",
  "reason": "short rationale"
}

User request:
{{input.UserMessage}}
""";
    }

    private static bool TryParseDecision(
        string content,
        out HermesDecision? decision,
        out string error)
    {
        decision = null;
        error = string.Empty;

        var json = ExtractJson(content);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "no JSON object found";
            return false;
        }

        try
        {
            decision = JsonSerializer.Deserialize<HermesDecision>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (decision == null)
            {
                error = "JSON parsed but result is null";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static WorkflowRunResult ApplyDecision(
        HermesDecision decision,
        WorkflowRunInput input)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        var action = (decision.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action == "select")
        {
            var selected = NormalizeToken(decision.SelectedWorkflow);
            if (selected.Length == 0)
                return new WorkflowRunResult(runId, false, "Hermes did not provide selected_workflow");

            var workflowPath = AevatarConfigFileHelper.ResolveFilePath(
                input.ConfigDirectory,
                AevatarConfigDirectory.Workflows,
                selected,
                WorkflowExtensions);
            if (workflowPath == null)
                return new WorkflowRunResult(runId, false, $"workflow '{selected}' not found");

            return new WorkflowRunResult(
                RunId: runId,
                Ok: true,
                Note: $"已选择工作流：{selected}",
                SelectedWorkflow: selected);
        }

        if (action == "create")
        {
            var workflow = decision.Workflow;

            var workflowName = NormalizeToken(workflow?.Name);
            if (workflowName.Length == 0)
                return new WorkflowRunResult(runId, false, "Hermes did not provide workflow.name");

            var format = (workflow?.Format ?? "yaml").Trim().ToLowerInvariant();
            var ext = format == "json" ? ".json" : ".yaml";
            var workflowDir = AevatarConfigFileHelper.GetDirectoryPath(
                input.ConfigDirectory,
                AevatarConfigDirectory.Workflows);
            var workflowPath = Path.Combine(workflowDir, $"{workflowName}{ext}");

            var resolvedWorkflow = AevatarConfigFileHelper.ResolveFilePath(
                input.ConfigDirectory,
                AevatarConfigDirectory.Workflows,
                workflowName,
                WorkflowExtensions);
            if (resolvedWorkflow == null)
            {
                return new WorkflowRunResult(
                    runId,
                    false,
                    $"workflow file not found: {workflowPath} (Hermes should create it via file_write)");
            }

            var createdAgents = new List<string>();
            var missingAgents = new List<string>();
            if (decision.Agents is { Count: > 0 })
            {
                foreach (var agent in decision.Agents)
                {
                    var role = GlobalAgentYamlRegistry.NormalizeRoleKey(agent?.Name);
                    if (role.Length == 0)
                        continue;

                    var agentPath = AevatarConfigFileHelper.ResolveFilePath(
                        input.ConfigDirectory,
                        AevatarConfigDirectory.Agents,
                        role,
                        AgentExtensions);
                    if (agentPath != null)
                        createdAgents.Add(role);
                    else
                        missingAgents.Add(role);
                }
            }

            if (missingAgents.Count > 0)
            {
                return new WorkflowRunResult(
                    runId,
                    false,
                    $"agent yaml missing: {string.Join(", ", missingAgents)} (Hermes should create them via file_write)");
            }

            var note = $"已创建工作流：{workflowName}";
            if (createdAgents.Count > 0)
                note += $"；新增角色：{string.Join(", ", createdAgents)}";

            return new WorkflowRunResult(
                RunId: runId,
                Ok: true,
                Note: note,
                SelectedWorkflow: workflowName,
                CreatedWorkflow: workflowName,
                CreatedAgentRoles: createdAgents);
        }

        return new WorkflowRunResult(runId, false, $"Hermes action unsupported: {action}");
    }

    private static IReadOnlyList<string> ListWorkflowNames(string configDir)
        => AevatarConfigFileHelper.ListFileBaseNames(
            configDir,
            AevatarConfigDirectory.Workflows,
            WorkflowExtensions);

    private static IReadOnlyList<string> ListRoleNames(WorkflowRunInput input)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in ListRoleNamesFromDir(Path.Combine(input.WorkingDirectory, "aevatar", "agents")))
            roles.Add(name);

        foreach (var name in AevatarConfigFileHelper.ListFileBaseNames(
                     input.ConfigDirectory,
                     AevatarConfigDirectory.Agents,
                     AgentExtensions))
        {
            var key = GlobalAgentYamlRegistry.NormalizeRoleKey(name);
            if (key.Length > 0)
                roles.Add(key);
        }

        return roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ListRoleNamesFromDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p =>
                {
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    return ext is ".yaml" or ".yml";
                })
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .Select(GlobalAgentYamlRegistry.NormalizeRoleKey)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeToken(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string? ExtractJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var fenceStart = trimmed.IndexOf('{');
            var fenceEnd = trimmed.LastIndexOf('}');
            if (fenceStart >= 0 && fenceEnd > fenceStart)
                return trimmed.Substring(fenceStart, fenceEnd - fenceStart + 1);
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return trimmed.Substring(start, end - start + 1);
    }

    private async Task<(bool Ok, IReadOnlyList<DslValidationError> Errors)> TryCompileWorkflowAsync(
        string workflowName,
        WorkflowRunInput input,
        CancellationToken ct)
    {
        var path = AevatarConfigFileHelper.ResolveFilePath(
            input.ConfigDirectory,
            AevatarConfigDirectory.Workflows,
            workflowName,
            WorkflowExtensions);
        if (path == null)
        {
            return (false, new[]
            {
                new DslValidationError("workflow.not_found", $"workflow '{workflowName}' not found", null)
            });
        }

        var raw = await File.ReadAllTextAsync(path, ct);
        var compiler = new PlatformMeshCompiler(
            localAgentRoot: input.WorkingDirectory,
            configAgentsDir: AevatarConfigFileHelper.GetDirectoryPath(
                input.ConfigDirectory,
                AevatarConfigDirectory.Agents));
        var compile = compiler.Compile(raw);
        return compile.Ok
            ? (true, Array.Empty<DslValidationError>())
            : (false, compile.Errors);
    }

    private async Task<WorkflowRunResult> TryRepairWorkflowAsync(
        string workflowName,
        IReadOnlyList<DslValidationError> errors,
        WorkflowRunInput input,
        CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        var errorSummary = FormatCompileErrors(errors);
        var prompt = BuildHermesRepairPrompt(workflowName, errorSummary, input);
        var response = await _runner.RunAsync("hermes", prompt, input, _runner.BuildHermesOptions(), ct);

        if (!TryParseDecision(response, out var decision, out var parseError))
        {
            return new WorkflowRunResult(
                runId,
                false,
                $"Hermes repair parse failed: {parseError}\n\nRaw:\n{response}");
        }

        var result = ApplyDecision(decision!, input);
        if (!result.Ok)
            return result;

        var finalName = result.CreatedWorkflow ?? result.SelectedWorkflow ?? workflowName;
        var check = await TryCompileWorkflowAsync(finalName, input, ct);
        if (!check.Ok)
        {
            var msg = FormatCompileErrors(check.Errors);
            return new WorkflowRunResult(
                runId,
                false,
                $"workflow compile failed: {msg}");
        }

        return result with { Note = $"已修复工作流：{finalName}" };
    }

    private static string BuildHermesRepairPrompt(
        string workflowName,
        string errors,
        WorkflowRunInput input)
    {
        var workflowPath = AevatarConfigFileHelper.ResolveFilePath(
            input.ConfigDirectory,
            AevatarConfigDirectory.Workflows,
            workflowName,
            WorkflowExtensions);

        return $$"""
You are Hermes. A workflow you created failed to compile.

Workflow name: {{workflowName}}
Workflow path: {{workflowPath ?? "(missing)"}}
Compile errors: {{errors}}

Task:
- Use file_read to inspect the workflow file.
- Use file_write to rewrite it into a valid DSL v0.1.
- Keep the workflow minimal (1-3 nodes).
- Output JSON ONLY using the same schema as before.
""";
    }

    private static string FormatCompileErrors(IReadOnlyList<DslValidationError> errors)
    {
        if (errors.Count == 0)
            return "mesh.compile_failed";

        return string.Join("; ", errors.Select(e =>
        {
            var path = string.IsNullOrWhiteSpace(e.Path) ? "" : $" ({e.Path})";
            var msg = string.IsNullOrWhiteSpace(e.Message) ? "" : $": {e.Message}";
            return $"{e.Code}{path}{msg}";
        }));
    }


    private sealed record HermesDecision(
        string? Action,
        string? SelectedWorkflow,
        HermesWorkflow? Workflow,
        List<HermesAgent>? Agents,
        string? Message,
        string? Reason);

    private sealed record HermesWorkflow(
        string? Name,
        string? Format,
        string? Content);

    private sealed record HermesAgent(
        string? Name,
        string? Content);
}
