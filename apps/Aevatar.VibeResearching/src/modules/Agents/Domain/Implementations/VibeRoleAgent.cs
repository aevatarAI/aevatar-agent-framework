using Microsoft.Extensions.Logging;
namespace Aevatar.VibeResearching.Agents;

// ============================================================
//  VibeRoleAgent
//
//  What:
//  - A generic "role-driven" agent used by Mesh orchestration for dynamic roles.
//
//  Why:
//  - Long-term, mesh nodes should reference roles (node.type == role).
//  - We want to support new roles without adding new C# agent classes.
//
//  How:
//  - The runtime (ResearchRuntime) loads ~/.aevatar/agents/{role}.yaml and applies:
//    - provider/model/temperature/max_tokens/stop_sequences
//    - system_prompt (plus default role framing)
// ============================================================

public sealed class VibeRoleAgent : VibeAgentBase
{
    public VibeRoleAgent()
    {
        // Keep prompt minimal; runtime will override based on YAML.
        SystemPrompt =
            """
            You are a role-driven agent in a mesh-orchestrated research workflow.

            Rules:
            - Keep output bounded and actionable.
            - If you rely on materials, cite [material:...] ids when available in the system prompt context.
            """;
    }
}
