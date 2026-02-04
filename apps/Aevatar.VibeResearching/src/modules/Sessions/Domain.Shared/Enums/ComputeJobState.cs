namespace Aevatar.VibeResearching.Sessions.Enums;

/// <summary>
/// State of a compute job execution.
/// Maps to SraComputeJobState in proto contracts.
/// </summary>
public enum ComputeJobState
{
    /// <summary>Unspecified state.</summary>
    Unspecified = 0,

    /// <summary>Job is queued for execution.</summary>
    Queued = 1,

    /// <summary>Job is currently running.</summary>
    Running = 2,

    /// <summary>Job completed successfully.</summary>
    Succeeded = 3,

    /// <summary>Job failed with errors.</summary>
    Failed = 4,

    /// <summary>Job was cancelled.</summary>
    Cancelled = 5
}
