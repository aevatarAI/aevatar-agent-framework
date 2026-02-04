using Aevatar.Agents.AI.Core;

namespace Aevatar.Agents.Cognitive.Researching.Runtime;

public interface IResearchingRuntime
{
    Task<(AIGAgentBase Agent, string AgentId)> GetPlannerAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct);

    Task<(AIGAgentBase Agent, string AgentId)> GetReasonerAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct);

    Task<(AIGAgentBase Agent, string AgentId)> GetResearchAssistantAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct);

    Task<(AIGAgentBase Agent, string AgentId)> GetLibrarianAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct);

    Task<(AIGAgentBase Agent, string AgentId)> GetVerifierAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct);

    Task<(AIGAgentBase Agent, string AgentId)> GetVerifierAgentAsync(
        string sessionId,
        string? providerName,
        string verifierKey,
        CancellationToken ct);

    Task<(AIGAgentBase Agent, string AgentId)> GetPaperEditorAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct);

    Task<(AIGAgentBase Agent, string AgentId)> GetDagBuilderAgentAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct);

    /// <summary>
    /// Dynamic role agent (role yaml in ~/.aevatar/agents/{role}.yaml).
    /// </summary>
    Task<(AIGAgentBase Agent, string AgentId)> GetRoleAgentAsync(
        string sessionId,
        string? providerName,
        string role,
        CancellationToken ct);

    Task<(IReadOnlyList<object> Tools, IReadOnlySet<string> McpToolNames)> RefreshToolsSnapshotAsync(
        string sessionId,
        string? providerName,
        CancellationToken ct);

    Task<bool> IsMcpToolAsync(string sessionId, string toolName, CancellationToken ct);
}
