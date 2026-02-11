namespace SisyphusMaker.Dtos;

/// <summary>
/// Wrapper for SSE event payloads emitted during verification.
/// </summary>
public sealed record SseEvent(string EventType, object Data);

/// <summary>
/// Payload for the "started" SSE event.
/// </summary>
public sealed record StartedEvent(string JobId, string VerificationType, int WorkerCount);

/// <summary>
/// Payload for the "phase" SSE event emitted when the cognitive workflow enters a new phase.
/// Phases: LOADING, INITIALIZING, ASSESS, DECOMPOSE, SOLVE, COMPOSE, EXECUTE, COMPLETE.
/// </summary>
public sealed record PhaseEvent(string Phase, string Message, float Progress);

/// <summary>
/// Payload for the "vote_round" SSE event from the cognitive voting process.
/// </summary>
public sealed record CognitiveVoteEvent(int Round, int VotesNeeded, int CurrentVotes, bool ConsensusReached);

/// <summary>
/// Payload for the "worker_done" SSE event emitted when a worker completes a proposal.
/// </summary>
public sealed record CognitiveWorkerDoneEvent(string WorkerId, string ProposalId, bool Success);

/// <summary>
/// Payload for the "error" SSE event emitted on unrecoverable errors.
/// </summary>
public sealed record ErrorEvent(string Error, string JobId, long DurationMs);
