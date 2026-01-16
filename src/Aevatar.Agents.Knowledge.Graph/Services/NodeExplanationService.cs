using Aevatar.Agents.Knowledge.Graph.Models;

namespace Aevatar.Agents.Knowledge.Graph.Services;

/// <summary>
/// Service for generating human-readable explanations of nodes.
/// This service now delegates to IGraphNode.Explain() for the actual explanation generation.
/// </summary>
public interface INodeExplanationService
{
    /// <summary>
    /// Generates a detailed explanation of any graph node.
    /// </summary>
    Task<NodeExplanation> ExplainNodeAsync(
        IGraphNode node,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a detailed explanation of a knowledge node.
    /// </summary>
    Task<NodeExplanation> ExplainKnowledgeNodeAsync(
        KnowledgeNode node,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a detailed explanation of a plan node.
    /// </summary>
    Task<NodeExplanation> ExplainPlanNodeAsync(
        PlanNode node,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of INodeExplanationService.
/// Delegates to IGraphNode.Explain() for consistent explanation generation.
/// </summary>
public sealed class NodeExplanationService : INodeExplanationService
{
    /// <inheritdoc />
    public Task<NodeExplanation> ExplainNodeAsync(
        IGraphNode node,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(snapshot);

        return Task.FromResult(node.Explain(snapshot));
    }

    /// <inheritdoc />
    public Task<NodeExplanation> ExplainKnowledgeNodeAsync(
        KnowledgeNode node,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(snapshot);

        return Task.FromResult(node.Explain(snapshot));
    }

    /// <inheritdoc />
    public Task<NodeExplanation> ExplainPlanNodeAsync(
        PlanNode node,
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(snapshot);

        return Task.FromResult(node.Explain(snapshot));
    }
}
