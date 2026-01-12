namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Predefined knowledge node types for categorizing knowledge in the graph.
/// </summary>
public enum KnowledgeNodeType
{
    /// <summary>Generic knowledge node.</summary>
    Generic = 0,

    // ===== Mathematics =====
    /// <summary>A mathematical axiom - a fundamental assumption.</summary>
    MathAxiom = 100,
    /// <summary>A mathematical theorem - a proven statement.</summary>
    MathTheorem = 101,
    /// <summary>A mathematical proof.</summary>
    MathProof = 102,
    /// <summary>A mathematical definition.</summary>
    MathDefinition = 103,
    /// <summary>A mathematical lemma - a helper theorem.</summary>
    MathLemma = 104,
    /// <summary>A mathematical corollary - a direct consequence of a theorem.</summary>
    MathCorollary = 105,

    // ===== Physics =====
    /// <summary>A physics law.</summary>
    PhysicsLaw = 200,
    /// <summary>A physics theory.</summary>
    PhysicsTheory = 201,
    /// <summary>A physics experiment result.</summary>
    PhysicsExperiment = 202,

    // ===== Biology =====
    /// <summary>A biology experiment result.</summary>
    BiologyExperiment = 300,
    /// <summary>A biological process description.</summary>
    BiologyProcess = 301,
    /// <summary>A biological structure description.</summary>
    BiologyStructure = 302,

    // ===== Chemistry =====
    /// <summary>A chemistry experiment result.</summary>
    ChemistryExperiment = 400,
    /// <summary>A chemical reaction description.</summary>
    ChemistryReaction = 401,

    // ===== Computer Science =====
    /// <summary>An algorithm description.</summary>
    CsAlgorithm = 500,
    /// <summary>A data structure description.</summary>
    CsDataStructure = 501,
    /// <summary>A software design pattern.</summary>
    CsDesignPattern = 502,

    // ===== General Research =====
    /// <summary>A research paper or publication.</summary>
    ResearchPaper = 600,
    /// <summary>A research dataset.</summary>
    ResearchDataset = 601,
    /// <summary>Research analysis results.</summary>
    ResearchAnalysis = 602,
    /// <summary>A research hypothesis.</summary>
    ResearchHypothesis = 603,

    // ===== Documentation =====
    /// <summary>A note or annotation.</summary>
    Note = 700,
    /// <summary>A summary or abstract.</summary>
    Summary = 701,
    /// <summary>A reference to external content.</summary>
    Reference = 702
}
