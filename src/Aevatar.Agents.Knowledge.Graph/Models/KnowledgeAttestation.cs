namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// A third-party attestation for a knowledge edge.
/// <para>
/// It records that "the holder of the private key corresponding to PubKey considers this edge correct",
/// by providing a signature over an application-defined canonical payload.
/// </para>
/// </summary>
public sealed class KnowledgeAttestation
{
    /// <summary>Public key identifier or material (recommended: stable encoding such as base64 or hex).</summary>
    public required string PubKey { get; init; }

    /// <summary>Signature bytes encoded as text (recommended: base64).</summary>
    public required string Signature { get; init; }
}