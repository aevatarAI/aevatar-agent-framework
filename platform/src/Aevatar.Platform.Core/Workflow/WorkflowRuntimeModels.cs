using Aevatar.Platform.Core.Config;

namespace Aevatar.Platform.Core.Workflow;

public sealed record WorkflowRunResult(
    string RunId,
    bool Ok,
    string Note,
    string? SelectedWorkflow = null,
    string? CreatedWorkflow = null,
    IReadOnlyList<string>? CreatedAgentRoles = null);

public sealed record WorkflowRunInput(
    string UserMessage,
    string ConfigDirectory,
    string ConfigPath,
    string SecretsPath,
    string WorkingDirectory,
    string? Profile,
    string? WorkflowName,
    string? DefaultProvider,
    string? DefaultModel,
    IReadOnlyList<string> AttachedFiles,
    ToolsConfig? ToolsConfig = null);
