namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Kind of a DAG node (knowledge vs plan).
/// Maps to SraDagNodeKind in proto contracts.
/// </summary>
public enum DagNodeKind
{
    /// <summary>Knowledge node: represents facts, theorems, hypotheses.</summary>
    Knowledge = 0,

    /// <summary>Plan node: represents research plan steps and milestones.</summary>
    Plan = 1
}
