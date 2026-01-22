using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.Configuration;
using Aevatar.CognitiveMesh.Dsl.Validation;

namespace Aevatar.Platform.Core.Workflow;

public sealed class HermesRouter
{
    private readonly RoleAgentRunner _runner;
    private static readonly IReadOnlyList<string> WorkflowExtensions = new[] { ".json", ".yaml", ".yml" };
    private static readonly IReadOnlyList<string> AgentExtensions = new[] { ".yaml", ".yml" };
    private const string AgentCreatorWorkflowName = "agent_creator";
    private const string AgentRouterWorkflowName = "agent_router";

    private enum HermesRouteMode
    {
        Router,
        Creator
    }

    public HermesRouter(RoleAgentRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<WorkflowRunResult> RouteAsync(WorkflowRunInput input, CancellationToken ct)
    {
        var runId = $"run_{Guid.NewGuid():N}";
        try
        {
            var mode = ResolveMode(input);
            var available = ListWorkflowNames(input.ConfigDirectory);
            var prompt = mode == HermesRouteMode.Creator
                ? BuildHermesCreatorPrompt(input, available)
                : BuildHermesRouterPrompt(input, available);
            var response = await _runner.RunAsync("hermes", prompt, input, _runner.BuildHermesOptions(), ct);
            return await ProcessHermesResponseAsync(response, runId, input, mode, ct);
        }
        catch (Exception ex)
        {
            return new WorkflowRunResult(runId, false, $"Hermes error: {ex.Message}");
        }
    }

    public async Task<WorkflowRunResult> RouteStreamingAsync(
        WorkflowRunInput input,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onDelta);
        var runId = $"run_{Guid.NewGuid():N}";
        try
        {
            var mode = ResolveMode(input);
            var available = ListWorkflowNames(input.ConfigDirectory);
            var prompt = mode == HermesRouteMode.Creator
                ? BuildHermesCreatorPrompt(input, available)
                : BuildHermesRouterPrompt(input, available);
            var sb = new StringBuilder();
            await foreach (var chunk in _runner.RunStreamAsync("hermes", prompt, input, _runner.BuildHermesOptions(), ct))
            {
                if (string.IsNullOrEmpty(chunk))
                    continue;
                sb.Append(chunk);
            }

            var response = sb.ToString();
            #region agent log
            DebugLog(
                "HermesRouter.cs:RouteStreamingAsync",
                "hermes_stream_response",
                new
                {
                    responseLen = response.Length,
                    startsWithFence = response.TrimStart().StartsWith("```", StringComparison.Ordinal)
                },
                input.ConfigDirectory,
                runId,
                "H1");
            #endregion
            return await ProcessHermesResponseAsync(response, runId, input, mode, ct);
        }
        catch (Exception ex)
        {
            return new WorkflowRunResult(runId, false, $"Hermes error: {ex.Message}");
        }
    }

    private async Task<WorkflowRunResult> ProcessHermesResponseAsync(
        string response,
        string runId,
        WorkflowRunInput input,
        HermesRouteMode mode,
        CancellationToken ct)
    {
            if (!TryParseDecision(response, out var decision, out var error))
            {
                var parseNote = $"Hermes parse failed: {error}\n\nRaw:\n{response}";
                return new WorkflowRunResult(runId, false, parseNote);
            }

        #region agent log
        DebugLog(
            "HermesRouter.cs:ProcessHermesResponseAsync",
            "decision_parsed",
            new
            {
                action = decision?.Action ?? string.Empty,
                selectedWorkflow = decision?.SelectedWorkflow ?? string.Empty,
                workflowName = decision?.Workflow?.Name ?? string.Empty,
                agentsCount = decision?.Agents?.Count ?? 0,
                messageLen = decision?.Message?.Length ?? 0,
                reasonLen = decision?.Reason?.Length ?? 0
            },
            input.ConfigDirectory,
            runId,
            "H1");
        #endregion

        if (mode == HermesRouteMode.Router &&
            TryBuildRouterRedirect(decision!, input, runId, out var redirect))
        {
            return redirect!;
        }

        if (mode == HermesRouteMode.Creator)
        {
            var selectedToken = (decision?.SelectedWorkflow ?? string.Empty).Trim();
            if (selectedToken.Equals(AgentCreatorWorkflowName, StringComparison.OrdinalIgnoreCase))
            {
                return new WorkflowRunResult(
                    runId,
                    false,
                    "agent_creator cannot select itself; please create or select a different workflow/agent.");
            }
        }

        AgentEnsureResult? agentEnsure = null;
        if (mode == HermesRouteMode.Creator)
        {
            var action = (decision?.Action ?? string.Empty).Trim().ToLowerInvariant();
            var selectedToken = (decision?.SelectedWorkflow ?? string.Empty).Trim();
            var selectedRole = string.Empty;
            if (action == "select")
            {
                if (IsAgentSelection(selectedToken, out var role))
                {
                    selectedRole = role;
                }
                else if (IsDirectSelection(selectedToken))
                {
                    var directRole = ResolveDirectRole(decision!, out var directError);
                    if (directRole.Length == 0)
                    {
                        var note = string.IsNullOrWhiteSpace(decision!.Message)
                            ? directError
                            : $"{decision!.Message!.Trim()}\n\n{directError}";
                        return new WorkflowRunResult(runId, false, note);
                    }

                    selectedRole = directRole;
                }
            }
            agentEnsure = EnsureAgentsFromDecision(decision!, input, runId, selectedRole);
            if (!agentEnsure.Ok)
            {
                var note = string.IsNullOrWhiteSpace(decision!.Message)
                    ? agentEnsure.Note
                    : $"{decision!.Message!.Trim()}\n\n{agentEnsure.Note}";
                return new WorkflowRunResult(runId, false, note);
            }
        }

            var result = ApplyDecision(decision!, input);
            var header = string.IsNullOrWhiteSpace(decision!.Message)
                ? result.Note
                : decision.Message!.Trim();
        if (string.IsNullOrWhiteSpace(header) &&
            (string.IsNullOrWhiteSpace(result.SelectedWorkflow) ||
             result.SelectedWorkflow.Equals("hermes", StringComparison.OrdinalIgnoreCase)))
        {
            header = BuildFallbackMessage(input.UserMessage);
        }
        if (agentEnsure is { Created.Count: > 0 })
        {
            var created = string.Join(", ", agentEnsure.Created);
            header = string.IsNullOrWhiteSpace(header)
                ? $"已创建角色：{created}"
                : $"{header}\n\n已创建角色：{created}";
            result = result with { CreatedAgentRoles = MergeCreatedAgents(result.CreatedAgentRoles, agentEnsure.Created) };
        }

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

    private static string BuildHermesPrompt(
        WorkflowRunInput input,
        IReadOnlyList<string> availableWorkflows)
    {
        return BuildHermesRouterPrompt(input, availableWorkflows);
    }

    private static HermesRouteMode ResolveMode(WorkflowRunInput input)
    {
        var name = (input.WorkflowName ?? string.Empty).Trim();
        if (name.Equals(AgentCreatorWorkflowName, StringComparison.OrdinalIgnoreCase))
            return HermesRouteMode.Creator;
        return HermesRouteMode.Router;
    }

    private static string BuildHermesRouterPrompt(
        WorkflowRunInput input,
        IReadOnlyList<string> availableWorkflows)
    {
        var workflowNames = availableWorkflows?.ToList() ?? new List<string>();
        EnsureToken(workflowNames, "direct");
        EnsureToken(workflowNames, AgentCreatorWorkflowName);

        var workflows = workflowNames.Count == 0
            ? "(none)"
            : string.Join(", ", workflowNames);

        var attached = input.AttachedFiles.Count == 0
            ? "(none)"
            : string.Join(", ", input.AttachedFiles);

        var roles = ListRoleNames(input);
        var roleText = roles.Count == 0 ? "(none)" : string.Join(", ", roles);

        return $$"""
You are Hermes, a workflow router.
Your task: select an existing workflow from ~/.aevatar/workflows or choose "direct" for a single-agent task.
If no existing workflow/agent fits, set selected_workflow="{{AgentCreatorWorkflowName}}" to enter the creation pipeline.

Context:
- available_workflows: {{workflows}}
- available_roles: {{roleText}}
- working_directory: {{input.WorkingDirectory}}
- global_agents_dir: {{Path.Combine(input.ConfigDirectory, "agents")}}
- local_agents_dir: {{Path.Combine(input.WorkingDirectory, "aevatar", "agents")}}
- attached_files: {{attached}}

Tools:
- file_read: read UTF-8 text within allowed roots.
- file_write: write UTF-8 text within allowed roots. (DO NOT use in router mode)
- mesh_normalize: validate + normalize workflow DSL content. (DO NOT use in router mode)

Requirements:
- Router mode MUST NOT create or overwrite any files.
- Use file_read to inspect existing workflows/agents when needed.
- If selecting, use action=select and set selected_workflow.
- If the task is single-agent, use selected_workflow="direct" and include exactly ONE agent in agents[] (name=role).
- Prefer "direct" for simple Q&A (e.g., time queries, translations, short explanations).
- If no suitable workflow/agent exists, set selected_workflow="{{AgentCreatorWorkflowName}}".
- If you must ask a clarifying question, set action="select", leave selected_workflow empty, and put the question in "message".
- Role names must be ASCII (letters/digits/underscore). Do NOT use Chinese in role names.
- Do NOT set selected_workflow to "agent:hermes" or "direct" with role=hermes.
- Respond in Chinese in the "message" field.
- Output JSON ONLY. No markdown, no extra text.

JSON schema:
{
  "action": "select",
  "selected_workflow": "workflow name | direct | {{AgentCreatorWorkflowName}} | agent:<role>",
  "workflow": null,
  "agents": [
    { "name": "role_name", "content": "" }
  ],
  "message": "short response to user",
  "reason": "short rationale"
}

User request:
{{input.UserMessage}}
""";
    }

    private static string BuildHermesCreatorPrompt(
        WorkflowRunInput input,
        IReadOnlyList<string> availableWorkflows)
    {
        var workflowNames = availableWorkflows?.ToList() ?? new List<string>();
        EnsureToken(workflowNames, "direct");
        EnsureToken(workflowNames, AgentRouterWorkflowName);

        var workflows = workflowNames.Count == 0
            ? "(none)"
            : string.Join(", ", workflowNames);

        var attached = input.AttachedFiles.Count == 0
            ? "(none)"
            : string.Join(", ", input.AttachedFiles);

        var roles = ListRoleNames(input);
        var roleText = roles.Count == 0 ? "(none)" : string.Join(", ", roles);

        return $$"""
You are Hermes, running in agent creation mode.
Your task: design and create missing agent YAML and a workflow DSL to satisfy the user's intent.

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
- mesh_normalize: validate + normalize workflow DSL content; returns canonical JSON/YAML or errors.

Creation pipeline (strict order):
1) Re-organize the user's request into a clear goal + constraints.
2) Decide whether a single agent can handle it; if yes, set selected_workflow="direct" and create/ensure ONE agent YAML.
3) If multi-step is required, draft agent YAMLs (missing roles only).
4) Draft workflow DSL v0.1 (minimal 1-3 nodes).
5) Call mesh_normalize with the workflow content and fix any errors until ok=true.
6) Use file_write to write agent YAML(s) to ~/.aevatar/agents and workflow to ~/.aevatar/workflows.

Requirements:
- Use file_read to inspect existing workflows/agents when needed.
- If selecting, use action=select and set selected_workflow.
- If creating, use action=create and provide workflow + optional agents.
- If the task is single-agent, use selected_workflow="direct" and include exactly ONE agent in agents[] (name=role, content only if missing). Do NOT create a workflow file for "direct".
- Workflow DSL v0.1 fields required: dsl_version, goal, strategy, budget, nodes, edges, constraints.
- goal must be an object: { name: string, success_metric: string? } (NO string shorthand).
- budget must be an object: { max_steps: int, token_limit: int } (NO string shorthand).
- strategy must be one of: cot/tot/got/uot_comb/uot_expl/uot_trans.
- node params must use "params" field (NOT "config").
- constraints must be list of objects: { type: string, value: any } (NOT string list).
- node.type must be one of: DivergentAgent, ConvergentAgent, WorkerAgent, CriticAgent, MetaAgent, or a role name (from YAML).
- Allowed constraint types: confidence_threshold, max_iterations (otherwise keep constraints empty).
- Role names must be ASCII (letters/digits/underscore). Do NOT use Chinese in role names.
- Do NOT set selected_workflow to "agent:hermes" or "direct" with role=hermes.
- Do NOT rely on params.instructions for behavior. Instead, create an agent YAML and set node.type to the role name.
- Keep workflow minimal (1-3 nodes) unless clearly needed.
- When action=create and workflow.content is present, ALWAYS call mesh_normalize and use its normalized output.
- If mesh_normalize returns ok=false, fix the DSL and retry normalization before final output.
- Default workflow.format to "json" unless the user explicitly asks for YAML.
- Respond in Chinese in the "message" field.
- Output JSON ONLY. No markdown, no extra text.

JSON schema:
{
  "action": "select" | "create",
  "selected_workflow": "workflow name | direct | agent:<role> | null",
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

    private static void EnsureToken(List<string> values, string token)
    {
        if (!values.Any(v => v.Equals(token, StringComparison.OrdinalIgnoreCase)))
            values.Add(token);
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
            var rawSelected = (decision.SelectedWorkflow ?? string.Empty).Trim();
            if (IsAgentSelection(rawSelected, out var role))
            {
                if (IsHermesRole(role))
                {
                    var note = string.IsNullOrWhiteSpace(decision.Message)
                        ? BuildFallbackMessage(input.UserMessage)
                        : decision.Message!.Trim();
                    return new WorkflowRunResult(
                        RunId: runId,
                        Ok: true,
                        Note: note,
                        SelectedWorkflow: "hermes");
                }
                return new WorkflowRunResult(
                    RunId: runId,
                    Ok: true,
                    Note: $"已选择角色：{role}",
                    SelectedWorkflow: $"agent:{role}");
            }
            if (IsDirectSelection(rawSelected))
            {
                var directRole = ResolveDirectRole(decision, out var directError);
                if (directRole.Length == 0)
                    return new WorkflowRunResult(runId, false, directError);

                if (IsHermesRole(directRole))
                {
                    var note = string.IsNullOrWhiteSpace(decision.Message)
                        ? BuildFallbackMessage(input.UserMessage)
                        : decision.Message!.Trim();
                    return new WorkflowRunResult(
                        RunId: runId,
                        Ok: true,
                        Note: note,
                        SelectedWorkflow: "hermes");
                }

                return new WorkflowRunResult(
                    RunId: runId,
                    Ok: true,
                    Note: $"已选择 direct：{directRole}",
                    SelectedWorkflow: $"agent:{directRole}");
            }
            var selected = NormalizeToken(rawSelected);
            if (selected.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(decision.Message))
                {
                    return new WorkflowRunResult(
                        RunId: runId,
                        Ok: true,
                        Note: decision.Message!.Trim(),
                        SelectedWorkflow: "hermes");
                }

                return new WorkflowRunResult(runId, false, "Hermes did not provide selected_workflow");
            }

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
            {
                if (decision.Agents is { Count: > 0 })
                {
                    var filtered = decision.Agents
                        .Where(a => !IsHermesRole(GlobalAgentYamlRegistry.NormalizeRoleKey(a?.Name)))
                        .ToList();
                    if (filtered.Count == 0)
                    {
                        var fallbackNote = string.IsNullOrWhiteSpace(decision.Message)
                            ? BuildFallbackMessage(input.UserMessage)
                            : decision.Message!.Trim();
                        return new WorkflowRunResult(
                            RunId: runId,
                            Ok: true,
                            Note: fallbackNote,
                            SelectedWorkflow: "hermes");
                    }

                    return CreateAgentsOnly(filtered, input, runId);
                }

                return new WorkflowRunResult(runId, false, "Hermes did not provide workflow.name");
            }

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
            #region agent log
            DebugLog(
                "HermesRouter.cs:ApplyDecision",
                "workflow_create_check",
                new
                {
                    workflowName,
                    workflowPath,
                    resolvedWorkflow,
                    resolvedExists = resolvedWorkflow != null,
                    configDir = input.ConfigDirectory
                },
                input.ConfigDirectory,
                runId,
                "H3");
            #endregion
            if (resolvedWorkflow == null)
            {
                var content = (workflow?.Content ?? string.Empty).Trim();
                if (content.Length == 0)
                {
                    return new WorkflowRunResult(
                        runId,
                        false,
                        $"workflow content missing: {workflowPath} (Hermes should provide content or call file_write)");
                }

                try
                {
                    Directory.CreateDirectory(workflowDir);
                    File.WriteAllText(workflowPath, content);
                    resolvedWorkflow = workflowPath;
                }
                catch (Exception ex)
            {
                return new WorkflowRunResult(
                    runId,
                    false,
                        $"workflow write failed: {ex.Message}");
                }
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
                    if (agentPath == null)
                    {
                        var content = (agent?.Content ?? string.Empty).Trim();
                        if (content.Length == 0)
                        {
                            missingAgents.Add(role);
                            continue;
                        }

                        try
                        {
                            var agentDir = AevatarConfigFileHelper.GetDirectoryPath(
                                input.ConfigDirectory,
                                AevatarConfigDirectory.Agents);
                            Directory.CreateDirectory(agentDir);
                            var path = Path.Combine(agentDir, $"{role}.yaml");
                            File.WriteAllText(path, content);
                            agentPath = path;
                        }
                        catch (Exception ex)
                        {
                            return new WorkflowRunResult(
                                runId,
                                false,
                                $"agent write failed: {ex.Message}");
                        }
                    }

                    if (agentPath != null)
                        createdAgents.Add(role);
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
- Use mesh_normalize to validate and normalize before final output.
- Keep the workflow minimal (1-3 nodes).
- Allowed constraint types: confidence_threshold, max_iterations (otherwise keep constraints empty).
- Role names must be ASCII (letters/digits/underscore). Do NOT use Chinese in role names.
- Do NOT set selected_workflow to "agent:hermes".
- Do NOT rely on params.instructions for behavior. Instead, create an agent YAML and set node.type to the role name.
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
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("selected_workflow")] string? SelectedWorkflow,
        [property: JsonPropertyName("workflow")] HermesWorkflow? Workflow,
        [property: JsonPropertyName("agents")] List<HermesAgent>? Agents,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record HermesWorkflow(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("format")] string? Format,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record HermesAgent(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record AgentEnsureResult(bool Ok, List<string> Created, string Note);

    private static AgentEnsureResult EnsureAgentsFromDecision(
        HermesDecision decision,
        WorkflowRunInput input,
        string runId,
        string selectedRole)
    {
        var created = new List<string>();
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (decision.Agents is { Count: > 0 })
        {
            foreach (var agent in decision.Agents)
            {
                var role = GlobalAgentYamlRegistry.NormalizeRoleKey(agent?.Name);
                if (role.Length == 0)
                    continue;
                roles[role] = (agent?.Content ?? string.Empty).Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedRole) && !roles.ContainsKey(selectedRole))
            roles[selectedRole] = string.Empty;

        if (roles.Count == 0)
            return new AgentEnsureResult(true, created, string.Empty);

        foreach (var (role, contentRaw) in roles)
        {
            var agentPath = AevatarConfigFileHelper.ResolveFilePath(
                input.ConfigDirectory,
                AevatarConfigDirectory.Agents,
                role,
                AgentExtensions);
            if (agentPath != null)
                continue;

            var content = contentRaw;
            var usedFallback = false;
            if (content.Length == 0)
            {
                if (!string.Equals(role, selectedRole, StringComparison.OrdinalIgnoreCase))
                {
                    var note = $"agent yaml missing: {role} (Hermes should provide content or call file_write)";
                    return new AgentEnsureResult(false, created, note);
                }

                content = BuildFallbackAgentYaml(role);
                usedFallback = true;
            }

            try
            {
                var agentDir = AevatarConfigFileHelper.GetDirectoryPath(
                    input.ConfigDirectory,
                    AevatarConfigDirectory.Agents);
                Directory.CreateDirectory(agentDir);
                var path = Path.Combine(agentDir, $"{role}.yaml");
                File.WriteAllText(path, content);
                created.Add(role);

                if (usedFallback)
                {
                    #region agent log
                    DebugLog(
                        "HermesRouter.cs:EnsureAgentsFromDecision",
                        "agent_fallback_written",
                        new { role },
                        input.ConfigDirectory,
                        runId,
                        "H3");
                    #endregion
                }
            }
            catch (Exception ex)
            {
                return new AgentEnsureResult(false, created, $"agent write failed: {ex.Message}");
            }
        }

        #region agent log
        DebugLog(
            "HermesRouter.cs:EnsureAgentsFromDecision",
            "agents_ensured",
            new { created = created.Count },
            input.ConfigDirectory,
            runId,
            "H3");
        #endregion

        return new AgentEnsureResult(true, created, string.Empty);
    }

    private static string BuildFallbackAgentYaml(string role)
    {
        var prompt = $"You are the '{role}' agent.";
        if (role.Contains("time", StringComparison.OrdinalIgnoreCase))
        {
            prompt += " Answer time/date questions and use tool time_now when available.";
        }

        return $$"""
id: "{{role}}"
name: "{{role}}"
version: "1.0"

persona:
  role: "{{role}}"
  style: "concise"

system_prompt: |
  {{prompt}}
""";
    }

    private static IReadOnlyList<string>? MergeCreatedAgents(
        IReadOnlyList<string>? existing,
        IReadOnlyList<string> added)
    {
        if ((existing == null || existing.Count == 0) && added.Count == 0)
            return existing;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existing != null)
        {
            foreach (var item in existing)
                if (!string.IsNullOrWhiteSpace(item))
                    set.Add(item);
        }

        foreach (var item in added)
            if (!string.IsNullOrWhiteSpace(item))
                set.Add(item);

        return set.ToList();
    }

    private static bool IsAgentSelection(string token, out string role)
    {
        role = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;
        if (!token.StartsWith("agent:", StringComparison.OrdinalIgnoreCase))
            return false;
        role = token["agent:".Length..].Trim();
        return role.Length > 0;
    }

    private static bool TryBuildRouterRedirect(
        HermesDecision decision,
        WorkflowRunInput input,
        string runId,
        out WorkflowRunResult? result)
    {
        result = null;

        var action = (decision.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action != "select")
        {
            result = BuildRouterRedirect(runId, input, decision, "router_mode_requires_select");
            return true;
        }

        var selected = (decision.SelectedWorkflow ?? string.Empty).Trim();
        if (IsDirectSelection(selected))
        {
            var role = ResolveDirectRole(decision, out var error);
            if (role.Length == 0)
            {
                result = BuildRouterRedirect(runId, input, decision, error);
                return true;
            }

            if (IsHermesRole(role))
            {
                result = BuildRouterRedirect(runId, input, decision, "direct_role_hermes_forbidden");
                return true;
            }

            if (!RoleExists(role, input))
            {
                result = BuildRouterRedirect(runId, input, decision, $"role_missing:{role}");
                return true;
            }

            return false;
        }

        if (IsAgentSelection(selected, out var agentRole))
        {
            if (IsHermesRole(agentRole))
            {
                result = BuildRouterRedirect(runId, input, decision, "agent_role_hermes_forbidden");
                return true;
            }

            if (!RoleExists(agentRole, input))
            {
                result = BuildRouterRedirect(runId, input, decision, $"role_missing:{agentRole}");
                return true;
            }

            return false;
        }

        var workflow = NormalizeToken(selected);
        if (workflow.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(decision.Message))
                return false;

            result = BuildRouterRedirect(runId, input, decision, "selected_workflow_missing");
            return true;
        }

        if (!WorkflowExists(workflow, input))
        {
            result = BuildRouterRedirect(runId, input, decision, $"workflow_missing:{workflow}");
            return true;
        }

        return false;
    }

    private static bool IsDirectSelection(string token)
        => token.Equals("direct", StringComparison.OrdinalIgnoreCase);

    private static string ResolveDirectRole(HermesDecision decision, out string error)
    {
        error = string.Empty;

        if (decision.Agents is not { Count: > 0 })
        {
            error = "direct requires exactly one agent role in agents[].";
            return string.Empty;
        }

        var roles = decision.Agents
            .Select(a => GlobalAgentYamlRegistry.NormalizeRoleKey(a?.Name))
            .Where(r => r.Length > 0)
            .ToList();

        if (roles.Count == 0)
        {
            error = "direct requires a non-empty agent name in agents[].";
            return string.Empty;
        }

        if (roles.Count == 1 && IsHermesRole(roles[0]))
        {
            error = "direct cannot use role 'hermes'.";
            return string.Empty;
        }

        var normalized = roles
            .Where(r => !IsHermesRole(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            error = "direct requires a non-hermes agent role.";
            return string.Empty;
        }

        if (normalized.Count > 1)
        {
            error = $"direct supports only one agent role (got: {string.Join(", ", normalized)}).";
            return string.Empty;
        }

        return normalized[0];
    }

    private static bool IsHermesRole(string role)
        => role.Equals("hermes", StringComparison.OrdinalIgnoreCase);

    private static WorkflowRunResult BuildRouterRedirect(
        string runId,
        WorkflowRunInput input,
        HermesDecision decision,
        string reason)
    {
        if (!WorkflowExists(AgentCreatorWorkflowName, input))
        {
            return new WorkflowRunResult(
                runId,
                false,
                $"workflow '{AgentCreatorWorkflowName}' not found; cannot switch. ({reason})");
        }

        var header = string.IsNullOrWhiteSpace(decision.Message)
            ? "当前没有合适的 workflow/agent，已切换到创建流程。"
            : decision.Message!.Trim();

        var note = string.IsNullOrWhiteSpace(reason)
            ? header
            : $"{header}\n\n(原因: {reason})";

        return new WorkflowRunResult(
            RunId: runId,
            Ok: true,
            Note: note,
            SelectedWorkflow: AgentCreatorWorkflowName);
    }

    private static bool RoleExists(string role, WorkflowRunInput input)
    {
        if (string.IsNullOrWhiteSpace(role))
            return false;

        var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
        if (key.Length == 0)
            return false;

        var localDir = Path.Combine(input.WorkingDirectory, "aevatar", "agents");
        if (File.Exists(Path.Combine(localDir, $"{key}.yaml")) ||
            File.Exists(Path.Combine(localDir, $"{key}.yml")))
            return true;

        var globalPath = AevatarConfigFileHelper.ResolveFilePath(
            input.ConfigDirectory,
            AevatarConfigDirectory.Agents,
            key,
            AgentExtensions);
        return globalPath != null;
    }

    private static bool WorkflowExists(string workflow, WorkflowRunInput input)
    {
        if (string.IsNullOrWhiteSpace(workflow))
            return false;

        var key = NormalizeToken(workflow);
        if (key.Length == 0)
            return false;

        var path = AevatarConfigFileHelper.ResolveFilePath(
            input.ConfigDirectory,
            AevatarConfigDirectory.Workflows,
            key,
            WorkflowExtensions);
        return path != null;
    }

    private static string BuildFallbackMessage(string? userMessage)
    {
        var text = (userMessage ?? string.Empty).Trim();
        if (text.Length == 0)
            return "请说明需要我处理的具体内容。";

        return "请提供更具体的需求或直接粘贴要处理的文本。";
    }

    private static WorkflowRunResult CreateAgentsOnly(
        IReadOnlyList<HermesAgent> agents,
        WorkflowRunInput input,
        string runId)
    {
        var createdAgents = new List<string>();
        var missingAgents = new List<string>();

        foreach (var agent in agents)
        {
            var role = GlobalAgentYamlRegistry.NormalizeRoleKey(agent?.Name);
            if (role.Length == 0)
                continue;
            if (IsHermesRole(role))
                continue;

            var agentPath = AevatarConfigFileHelper.ResolveFilePath(
                input.ConfigDirectory,
                AevatarConfigDirectory.Agents,
                role,
                AgentExtensions);
            if (agentPath == null)
            {
                var content = (agent?.Content ?? string.Empty).Trim();
                if (content.Length == 0)
                {
                    missingAgents.Add(role);
                    continue;
                }

                try
                {
                    var agentDir = AevatarConfigFileHelper.GetDirectoryPath(
                        input.ConfigDirectory,
                        AevatarConfigDirectory.Agents);
                    Directory.CreateDirectory(agentDir);
                    var path = Path.Combine(agentDir, $"{role}.yaml");
                    File.WriteAllText(path, content);
                    agentPath = path;
                }
                catch (Exception ex)
                {
                    return new WorkflowRunResult(
                        runId,
                        false,
                        $"agent write failed: {ex.Message}");
                }
            }

            if (agentPath != null)
                createdAgents.Add(role);
        }

        if (missingAgents.Count > 0)
        {
            return new WorkflowRunResult(
                runId,
                false,
                $"agent yaml missing: {string.Join(", ", missingAgents)} (Hermes should create them via file_write)");
        }

        var selectedRole = createdAgents.FirstOrDefault() ?? string.Empty;
        if (selectedRole.Length == 0)
            return new WorkflowRunResult(runId, false, "Hermes did not provide agents");

        var note = $"已创建角色：{string.Join(", ", createdAgents)}";
        return new WorkflowRunResult(
            RunId: runId,
            Ok: true,
            Note: note,
            SelectedWorkflow: $"agent:{selectedRole}",
            CreatedAgentRoles: createdAgents);
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
