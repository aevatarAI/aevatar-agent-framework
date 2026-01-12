using System.Text.Json.Serialization;

namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Assertion status of a knowledge node.
/// <para>
/// - Plan: tentative/provisional knowledge item (may be revised).
/// - Knowledge: asserted knowledge item that can be attested by signatures.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KnowledgeNodeKind
{
    /// <summary>Working plan / provisional node.</summary>
    Plan = 0,

    /// <summary>Knowledge / asserted node.</summary>
    Knowledge = 1
}