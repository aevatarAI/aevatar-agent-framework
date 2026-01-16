using System.Collections.Generic;

namespace Aevatar.CognitiveMesh.Dsl.Options;

/// <summary>
/// Options that describe guardrails for the DSL compiler.
/// </summary>
public sealed class CognitiveDslOptions
{
    private static readonly string[] DefaultAgentTypes =
    [
        "DivergentAgent",
        "ConvergentAgent",
        "WorkerAgent",
        "CriticAgent",
        "MetaAgent"
    ];

    private static readonly string[] DefaultConstraintTypes =
    [
        "confidence_threshold",
        "max_iterations"
    ];

    /// <summary>
    /// Default guardrail options aligned with the published DSL specification.
    /// </summary>
    public static CognitiveDslOptions Default { get; } = new();

    public CognitiveDslOptions()
    {
        AllowedAgentTypes = new HashSet<string>(DefaultAgentTypes, StringComparer.OrdinalIgnoreCase);
        AllowedConstraintTypes = new HashSet<string>(DefaultConstraintTypes, StringComparer.OrdinalIgnoreCase);
    }

    private CognitiveDslOptions(
        IEnumerable<string> allowedAgentTypes,
        IEnumerable<string> allowedConstraintTypes,
        string metaAgentTypeName)
    {
        AllowedAgentTypes = new HashSet<string>(allowedAgentTypes, StringComparer.OrdinalIgnoreCase);
        AllowedConstraintTypes = new HashSet<string>(allowedConstraintTypes, StringComparer.OrdinalIgnoreCase);
        MetaAgentTypeName = metaAgentTypeName;
    }

    /// <summary>
    /// All node types that can be instantiated by the DSL.
    /// </summary>
    public IReadOnlySet<string> AllowedAgentTypes { get; init; }

    /// <summary>
    /// Allowed constraint identifiers.
    /// </summary>
    public IReadOnlySet<string> AllowedConstraintTypes { get; init; }

    /// <summary>
    /// Canonical agent type name used to fulfill Transformative strategy requirements.
    /// </summary>
    public string MetaAgentTypeName { get; init; } = "MetaAgent";

    /// <summary>
    /// Creates a clone of the options with optional overrides.
    /// </summary>
    public CognitiveDslOptions With(
        IEnumerable<string>? allowedAgentTypes = null,
        IEnumerable<string>? allowedConstraintTypes = null,
        string? metaAgentTypeName = null)
    {
        var nextAgents = allowedAgentTypes is null
            ? AllowedAgentTypes
            : new HashSet<string>(allowedAgentTypes, StringComparer.OrdinalIgnoreCase);

        var nextConstraints = allowedConstraintTypes is null
            ? AllowedConstraintTypes
            : new HashSet<string>(allowedConstraintTypes, StringComparer.OrdinalIgnoreCase);

        return new CognitiveDslOptions(
            nextAgents,
            nextConstraints,
            metaAgentTypeName ?? MetaAgentTypeName);
    }
}

