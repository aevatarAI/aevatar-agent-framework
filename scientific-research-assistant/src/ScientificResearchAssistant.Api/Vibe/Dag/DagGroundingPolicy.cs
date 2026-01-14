using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe.Dag;

// ============================================================
//  DAG Grounding Policy (Configurable)
//
//  Purpose:
//  - Decide which DAG nodes are safe to include as "grounded context"
//    in the Research Assistant system prompt.
//
//  Why:
//  - Make filtering configurable (min attestations, required pubkeys, etc.)
//    without code changes.
// ============================================================

public sealed class DagGroundingOptions
{
    public const string SectionName = "Vibe:DagGrounding";

    /// <summary>
    /// Minimal attestation count required for a node to be considered grounded.
    /// <para>Default: 0 (all knowledge nodes are included for cross-session sharing).</para>
    /// </summary>
    public int MinAttestations { get; set; } = 0;

    /// <summary>
    /// If non-empty, at least one attestation pubkey must match one of these values.
    /// <para>
    /// Recommended encoding: hex/base64 (must match what attestations store).
    /// </para>
    /// </summary>
    public List<string> RequiredPubKeys { get; set; } = new();
}

public interface IDagGroundingPolicy
{
    bool ShouldIncludeForGrounding(SraDagNode node);
}

internal sealed class DefaultDagGroundingPolicy : IDagGroundingPolicy
{
    private readonly IOptionsMonitor<DagGroundingOptions> _options;

    public DefaultDagGroundingPolicy(IOptionsMonitor<DagGroundingOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool ShouldIncludeForGrounding(SraDagNode node)
    {
        if (node == null) return false;

        // Only knowledge nodes can be grounded.
        if (node.Kind != SraDagNodeKind.Knowledge) return false;

        var opts = _options.CurrentValue ?? new DagGroundingOptions();
        var min = Math.Max(0, opts.MinAttestations);

        var atts = node.Attestations;
        var count = atts?.Count ?? 0;
        if (count < min) return false;

        // Pubkey constraint (optional):
        // If configured, accept if ANY required pubkey appears in attestations.
        var required = (opts.RequiredPubKeys ?? new List<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (required.Count == 0) return true;

        for (var i = 0; i < count; i++)
        {
            var pk = (atts![i]?.Pubkey ?? string.Empty).Trim();
            if (pk.Length == 0) continue;
            if (required.Contains(pk, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}


