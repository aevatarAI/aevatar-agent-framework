namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Type of a DAG node in the knowledge graph.
/// Maps to SraDagNodeType in proto contracts.
/// </summary>
public enum DagNodeType
{
    /// <summary>Unspecified node type.</summary>
    Unspecified = 0,

    /// <summary>Axiom: fundamental truth or established fact.</summary>
    Axiom = 1,

    /// <summary>Theorem: derived truth proven from axioms and other theorems.</summary>
    Theorem = 2,

    /// <summary>Assumption: working assumption for the research.</summary>
    Assumption = 3,

    /// <summary>Hypothesis: proposed explanation to be tested.</summary>
    Hypothesis = 4,

    /// <summary>Unknown or generic node type.</summary>
    Unknown = 5
}
