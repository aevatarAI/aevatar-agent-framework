// ============================================================
//  Worker Inference - Extract workers from event stream
//
//  When backend doesn't provide explicit worker list,
//  we infer workers from worker_id fields in events.
// ============================================================

import type { ClassifiedEvent, WorkerVoteInfo, VotingStatus } from '@/types'

// ============================================================
//  Worker Colors (consistent with Maker design)
// ============================================================

const WORKER_COLORS = [
  '#22c55e', // Green
  '#3b82f6', // Blue
  '#f59e0b', // Amber
  '#ec4899', // Pink
  '#8b5cf6', // Purple
  '#06b6d4', // Cyan
  '#f97316', // Orange
  '#84cc16', // Lime
]

// ============================================================
//  Infer Workers from Events
// ============================================================

export interface InferredWorker {
  id: string
  name: string
  eventCount: number
  color: string
  isLeader: boolean
  lastSeen: number
}

/**
 * Extract unique workers from workflow events based on worker_id field
 * Only considers events from the LATEST run_id to prevent stale workers
 */
export function inferWorkersFromEvents(events: ClassifiedEvent[]): InferredWorker[] {
  const workerMap = new Map<string, InferredWorker>()
  let winnerWorkerId: string | undefined

  // ============================================================
  //  CRITICAL: Find the latest run_id to isolate workers
  //  This prevents stale workers from previous runs showing up
  //  when user triggers a new session with different worker count
  // ============================================================
  let latestRunId: string | undefined
  let latestRunTimestamp = 0
  
  for (const event of events) {
    const runId = event.raw.fields.run_id as string | undefined
    if (runId && event.timestamp > latestRunTimestamp) {
      latestRunId = runId
      latestRunTimestamp = event.timestamp
    }
  }

  // Filter events to only include the latest run
  const filteredEvents = latestRunId 
    ? events.filter(e => e.raw.fields.run_id === latestRunId)
    : events

  // First pass: collect all workers and find winner
  for (const event of filteredEvents) {
    const workerId = event.raw.fields.worker_id
    const winnerProposalId = event.raw.fields.winner_proposal_id

    // Track winner worker (winner_proposal_id often contains worker id)
    if (winnerProposalId) {
      // winner_proposal_id format: "worker-xxx:proposal-yyy" or just worker id
      const match = winnerProposalId.match(/^(worker[^:]*)/i)
      if (match) {
        winnerWorkerId = match[1]
      } else {
        winnerWorkerId = winnerProposalId.split(':')[0]
      }
    }

    if (!workerId) continue

    const existing = workerMap.get(workerId)
    if (existing) {
      existing.eventCount++
      existing.lastSeen = Math.max(existing.lastSeen, event.timestamp)
    } else {
      workerMap.set(workerId, {
        id: workerId,
        name: formatWorkerName(workerId),
        eventCount: 1,
        color: WORKER_COLORS[workerMap.size % WORKER_COLORS.length],
        isLeader: false,
        lastSeen: event.timestamp,
      })
    }
  }

  // Second pass: mark leader
  const workers = Array.from(workerMap.values())
  
  if (winnerWorkerId) {
    const leader = workers.find(w => 
      w.id === winnerWorkerId || 
      w.id.includes(winnerWorkerId) ||
      winnerWorkerId.includes(w.id)
    )
    if (leader) {
      leader.isLeader = true
    }
  } else if (workers.length > 0) {
    // Fallback: worker with most events is tentative leader
    const sorted = [...workers].sort((a, b) => b.eventCount - a.eventCount)
    if (sorted[0].eventCount > sorted[1]?.eventCount) {
      sorted[0].isLeader = true
    }
  }

  // Sort: leader first, then by event count
  return workers.sort((a, b) => {
    if (a.isLeader !== b.isLeader) return a.isLeader ? -1 : 1
    return b.eventCount - a.eventCount
  })
}

/**
 * Format worker ID into display name
 * "worker-0" -> "Worker 1"
 * "worker-deepseek-1" -> "DeepSeek-1"
 * "coordinator" -> "Coordinator"
 * "task-gpt4o" -> "GPT4O"
 */
function formatWorkerName(workerId: string): string {
  // Handle coordinator
  if (workerId.toLowerCase() === 'coordinator') {
    return 'Coordinator'
  }

  // Remove common prefixes
  let name = workerId
    .replace(/^(worker[-_]?|task[-_]?)/i, '')
    .trim()

  if (!name) return workerId

  // Handle pure numeric IDs from real backend: "0" -> "Worker 1"
  if (/^\d+$/.test(name)) {
    return `Worker ${parseInt(name) + 1}`
  }

  // Capitalize and format compound names
  return name
    .split(/[-_]/)
    .map(part => {
      // Keep numbers as-is, capitalize words
      if (/^\d+$/.test(part)) return part
      // Handle model names
      if (/^(gpt|claude|deepseek|llama|gemini)/i.test(part)) {
        return part.charAt(0).toUpperCase() + part.slice(1).toLowerCase()
      }
      return part.charAt(0).toUpperCase() + part.slice(1)
    })
    .join('-')
}

// ============================================================
//  Convert Inferred Workers to VotingStatus format
// ============================================================

/**
 * Convert inferred workers to WorkerVoteInfo array
 * Event count is used as a proxy for "activity" (not actual votes)
 */
export function inferredWorkersToVoteInfo(workers: InferredWorker[]): WorkerVoteInfo[] {
  return workers.map(w => ({
    id: w.id,
    name: w.name,
    votes: w.eventCount, // Using event count as activity metric
    color: w.color,
    isLeader: w.isLeader,
  }))
}

/**
 * Build a minimal VotingStatus from events when backend doesn't provide full data
 * Only considers events from the LATEST run_id for accurate status
 */
export function buildInferredVotingStatus(
  events: ClassifiedEvent[],
  existingStatus: VotingStatus | null
): VotingStatus {
  const inferredWorkers = inferWorkersFromEvents(events)
  const workers = inferredWorkersToVoteInfo(inferredWorkers)

  // ============================================================
  //  CRITICAL: Filter to latest run_id for accurate voting status
  //  This ensures voting rounds and consensus state reflect
  //  the current run, not accumulated from all past runs
  // ============================================================
  let latestRunId: string | undefined
  let latestRunTimestamp = 0
  
  for (const event of events) {
    const runId = event.raw.fields.run_id as string | undefined
    if (runId && event.timestamp > latestRunTimestamp) {
      latestRunId = runId
      latestRunTimestamp = event.timestamp
    }
  }

  const filteredEvents = latestRunId 
    ? events.filter(e => e.raw.fields.run_id === latestRunId)
    : events

  // Extract vote info from filtered events (latest run only)
  let round = 0
  let maxRounds = 10
  let k = 3
  let mode: 'semantic' | 'hash' = 'semantic'
  let clusterCount = 0
  let consensusReached = false
  let redFlagCount = 0

  for (const event of filteredEvents) {
    const fields = event.raw.fields
    const phase = event.raw.phase?.toLowerCase() ?? ''
    
    if (fields.vote_round !== undefined) round = Math.max(round, fields.vote_round)
    if (fields.vote_max_rounds !== undefined) maxRounds = fields.vote_max_rounds
    if (fields.vote_k !== undefined) k = fields.vote_k
    if (fields.winner_semantic !== undefined) mode = fields.winner_semantic ? 'semantic' : 'hash'
    if (fields.winner_cluster_count !== undefined) clusterCount = fields.winner_cluster_count
    if (fields.winner_is_consensus) consensusReached = true
    // Detect red flags from phase (backend doesn't send red_flag_reason field)
    if (phase === 'red_flag' || phase.includes('redflag')) redFlagCount++
  }

  // Merge with existing status if available
  return {
    round: existingStatus?.round ?? round,
    maxRounds: existingStatus?.maxRounds ?? maxRounds,
    k: existingStatus?.k ?? k,
    mode: existingStatus?.mode ?? mode,
    redFlagCount: existingStatus?.redFlagCount ?? redFlagCount,
    clusterCount: existingStatus?.clusterCount ?? clusterCount,
    // Use existing workers if available, otherwise use inferred
    workers: (existingStatus?.workers?.length ?? 0) > 0 
      ? existingStatus!.workers 
      : workers,
    consensusReached: existingStatus?.consensusReached ?? consensusReached,
    winner: existingStatus?.winner,
  }
}

// ============================================================
//  Check if we have real worker data or need inference
// ============================================================

export function hasRealWorkerData(votingStatus: VotingStatus | null): boolean {
  return (votingStatus?.workers?.length ?? 0) > 0
}

export function needsWorkerInference(
  votingStatus: VotingStatus | null,
  events: ClassifiedEvent[]
): boolean {
  // Need inference if no workers but we have events with worker_id
  if (hasRealWorkerData(votingStatus)) return false
  return events.some(e => e.raw.fields.worker_id)
}
