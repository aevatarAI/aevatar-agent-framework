using System.Text.Json.Serialization;

namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Execution status of a PlanNode in the research workflow.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlanNodeStatus
{
    /// <summary>
    /// Plan step has not yet started.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Plan step is currently being worked on by the dag_builder agent.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Plan step has been successfully completed.
    /// </summary>
    Completed = 2
}
