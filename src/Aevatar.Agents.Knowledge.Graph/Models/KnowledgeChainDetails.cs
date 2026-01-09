namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Result of getting knowledge chain details, containing both the structured chain
/// and a human-readable text description in Markdown format.
/// </summary>
public sealed class KnowledgeChainDetails
{
    /// <summary>
    /// The structured knowledge chain with nodes organized by inference levels.
    /// </summary>
    public required KnowledgeChain Chain { get; init; }

    /// <summary>
    /// A human-readable Markdown description of the knowledge chain,
    /// formatted as a mini research paper with abstract, derivation path,
    /// and detailed node information.
    /// </summary>
    public required string Description { get; init; }
}
