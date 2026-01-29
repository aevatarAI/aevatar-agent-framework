using Aevatar.Agents.AI.Core;

namespace Aevatar.Agents.Cognitive.Execution;

public sealed class CognitiveEventModuleFactory : IEventModuleFactory
{
    public bool TryCreate(string name, out IEventModule module)
    {
        var key = (name ?? string.Empty).Trim().ToLowerInvariant();
        switch (key)
        {
            case "step_execution_handler":
            case "step_executor":
                module = new StepExecutionModule(new CognitiveStepExecutionHandler());
                return true;
            case CoordinatorWorkflowEventModule.ModuleName:
                module = new CoordinatorWorkflowEventModule();
                return true;
            case CoordinatorParallelEventModule.ModuleName:
                module = new CoordinatorParallelEventModule();
                return true;
            case "coordinator_llm":
                module = new CoordinatorStepModule(
                    name: "coordinator_llm",
                    stepType: "llm_call",
                    execute: (agent, step, prompt, system, ct) =>
                        agent.ExecuteLlmCallDirectAsync(step, prompt, system));
                return true;
            case "coordinator_vote":
                module = new CoordinatorStepModule(
                    name: "coordinator_vote",
                    stepType: "vote",
                    execute: (agent, step, _, _, _) => agent.ExecuteVoteAsync(step));
                return true;
            case "coordinator_fan_out":
                module = new CoordinatorStepModule(
                    name: "coordinator_fan_out",
                    stepType: "fan_out",
                    execute: (agent, step, _, _, _) => agent.ExecuteFanOutAsync(step));
                return true;
            case "coordinator_parallel":
                module = new CoordinatorStepModule(
                    name: "coordinator_parallel",
                    stepType: "parallel",
                    execute: (agent, step, _, _, _) => agent.ExecuteParallelAsync(step));
                return true;
            case "coordinator_workflow_call":
                module = new CoordinatorStepModule(
                    name: "coordinator_workflow_call",
                    stepType: "workflow_call",
                    execute: (agent, step, _, _, _) => agent.ExecuteWorkflowCallAsync(step));
                return true;
            case "coordinator_conditional":
                module = new CoordinatorStepModule(
                    name: "coordinator_conditional",
                    stepType: "conditional",
                    execute: (agent, step, _, _, _) => agent.ExecuteConditionalAsync(step));
                return true;
            case "coordinator_checkpoint":
                module = new CoordinatorStepModule(
                    name: "coordinator_checkpoint",
                    stepType: "checkpoint",
                    execute: (agent, step, _, _, _) => agent.ExecuteCheckpointAsync(step));
                return true;
            case "coordinator_assign":
                module = new CoordinatorStepModule(
                    name: "coordinator_assign",
                    stepType: "assign",
                    execute: (agent, step, _, _, _) => agent.ExecuteAssignAsync(step));
                return true;
            case "coordinator_transform":
                module = new CoordinatorStepModule(
                    name: "coordinator_transform",
                    stepType: "transform",
                    execute: (agent, step, _, _, _) => agent.ExecuteTransformAsync(step));
                return true;
            case "coordinator_retrieve_facts":
                module = new CoordinatorStepModule(
                    name: "coordinator_retrieve_facts",
                    stepType: "retrieve_facts",
                    execute: (agent, step, _, _, _) => agent.ExecuteRetrieveFactsAsync(step));
                return true;
            case "coordinator_workspace_read_file":
                module = new CoordinatorStepModule(
                    name: "coordinator_workspace_read_file",
                    stepType: "workspace_read_file",
                    execute: (agent, step, _, _, _) => agent.ExecuteWorkspaceReadFileAsync(step));
                return true;
            case "coordinator_workspace_code_search":
                module = new CoordinatorStepModule(
                    name: "coordinator_workspace_code_search",
                    stepType: "workspace_code_search",
                    execute: (agent, step, _, _, _) => agent.ExecuteWorkspaceCodeSearchAsync(step));
                return true;
            case "coordinator_workspace_apply_patch":
                module = new CoordinatorStepModule(
                    name: "coordinator_workspace_apply_patch",
                    stepType: "workspace_apply_patch",
                    execute: (agent, step, _, _, _) => agent.ExecuteWorkspaceApplyPatchAsync(step));
                return true;
            case "coordinator_sandbox_command":
                module = new CoordinatorStepModule(
                    name: "coordinator_sandbox_command",
                    stepType: "sandbox_command",
                    execute: (agent, step, _, _, _) => agent.ExecuteSandboxCommandAsync(step));
                return true;
            default:
                module = null!;
                return false;
        }
    }
}
