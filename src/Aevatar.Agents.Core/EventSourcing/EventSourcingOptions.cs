namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Configuration options for EventSourcing.
/// Maps to appsettings.json section "EventSourcing".
/// </summary>
public class EventSourcingOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "EventSourcing";

    /// <summary>
    /// Enable or disable EventSourcing.
    /// When disabled, agents use simple StateStore persistence instead.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of events between automatic snapshots.
    /// Default: 10
    /// </summary>
    public int SnapshotFrequency { get; set; } = 10;

    /// <summary>
    /// EventStore provider type.
    /// Supported: "Memory", "MongoDB", "Orleans"
    /// Default: "MongoDB"
    /// </summary>
    public string Provider { get; set; } = "MongoDB";
}
