namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Constants for knowledge graph and DAG operations.
/// </summary>
public static class KnowledgeConsts
{
    /// <summary>
    /// Maximum number of nodes to include in DAG list operations.
    /// </summary>
    public const int MaxNodesForList = 2000;

    /// <summary>
    /// Maximum number of edges to include in DAG list operations.
    /// </summary>
    public const int MaxEdgesForList = 8000;

    /// <summary>
    /// Maximum number of staged mutations to list.
    /// </summary>
    public const int MaxStagedList = 80;

    /// <summary>
    /// Maximum number of attestations to include per node.
    /// </summary>
    public const int MaxAttestationsPerNode = 50;

    /// <summary>
    /// Maximum number of attestations to show in list view.
    /// </summary>
    public const int MaxAttestationsForDisplay = 20;

    /// <summary>
    /// Maximum length for node label.
    /// </summary>
    public const int MaxNodeLabelLength = 200;

    /// <summary>
    /// Maximum length for node proof/description.
    /// </summary>
    public const int MaxNodeProofLength = 1200;

    /// <summary>
    /// Default edge type for dependencies.
    /// </summary>
    public const string DefaultEdgeType = "depends_on";

    /// <summary>
    /// DAG artifacts directory name.
    /// </summary>
    public const string DagArtifactsDir = "dag";

    /// <summary>
    /// DAG snapshot file name.
    /// </summary>
    public const string DagSnapshotFile = "snapshot.json";

    /// <summary>
    /// DAG staged directory name.
    /// </summary>
    public const string DagStagedDir = "staged";

    /// <summary>
    /// DAG consensus directory name.
    /// </summary>
    public const string DagConsensusDir = "consensus";

    /// <summary>
    /// Secrets key for DAG owner public key.
    /// </summary>
    public const string DagOwnerPublicKeySecretsKey = "Crypto:EcdsaSecp256k1:PublicKeyHex";
}
