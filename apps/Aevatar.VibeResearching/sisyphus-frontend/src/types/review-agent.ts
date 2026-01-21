/**
 * TypeScript types for the Review Agent feature.
 * Matches the API schema from contracts/review-agent-api.yaml
 */

// ========== Enums ==========

export type ReviewAgentStatusValue =
  | 'Idle'
  | 'WorkingReviewRound'
  | 'WorkingCleanupRound'
  | 'Error';

export type ReviewResult = 'Passed' | 'Failed' | 'Skipped';

export type AgentRole = 'coordinator' | 'worker';

// ========== API Response Types ==========

export interface ReviewAgentStatus {
  status: ReviewAgentStatusValue;
  currentIterationId: string | null;
  lastCompletedAt: string | null;
  nextScheduledAt: string | null;
  nodesReviewed: number;
  nodesPending: number;
  nodesDeactivated: number;
  nodesRemoved?: number;
  errorMessage: string | null;
}

export interface ReviewAgentSettings {
  iterationIntervalMinutes: number;
  outOfDateThresholdMinutes: number;
  toDeleteThresholdMinutes: number;
  llmProviderName: string;
  perNodeTimeoutSeconds?: number;
}

export interface ReviewAgentSettingsUpdate {
  iterationIntervalMinutes?: number;
  outOfDateThresholdMinutes?: number;
  toDeleteThresholdMinutes?: number;
  llmProviderName?: string;
  perNodeTimeoutSeconds?: number;
}

export interface ReviewLogEntry {
  entryId: string;
  nodeId: string;
  nodeLabel: string;
  dependencies: string[];
  reviewResult: ReviewResult;
  deactivatedReason: string | null;
  timestamp: string;
  verificationContent: string | null;
}

export interface ReviewIteration {
  iterationId: string;
  startedAt: string;
  completedAt: string | null;
  nodesReviewed: number;
  nodesValid: number;
  nodesDeactivated: number;
  nodesRemoved: number;
  entries: ReviewLogEntry[];
}

export interface ReviewIterationSummary {
  iterationId: string;
  startedAt: string;
  completedAt: string | null;
  nodesReviewed: number;
  nodesValid: number;
  nodesDeactivated: number;
  nodesRemoved: number;
}

export interface IterationListResponse {
  iterations: ReviewIterationSummary[];
  total: number;
  limit: number;
  offset: number;
}

// ========== SSE Event Types ==========

export interface StatusChangeEvent {
  type: 'status_change';
  timestamp: string;
  status: ReviewAgentStatusValue;
  nextScheduledAt: string | null;
}

export interface NodeReviewProgressEvent {
  type: 'node_review_progress';
  timestamp: string;
  nodeId: string;
  nodeLabel: string;
  nodesReviewed: number;
  nodesPending: number;
  nodesDeactivated: number;
  result: ReviewResult | null;
}

export interface TokenStreamEvent {
  type: 'token_stream';
  timestamp: string;
  agentId: string;
  agentRole: AgentRole;
  token: string;
  isComplete: boolean;
}

export interface IterationCompleteEvent {
  type: 'iteration_complete';
  timestamp: string;
  iterationId: string;
  summary: ReviewIterationSummary;
}

export interface CleanupProgressEvent {
  type: 'cleanup_progress';
  timestamp: string;
  nodesRemoved: number;
  removedNodes: Array<{
    nodeId: string;
    nodeLabel: string;
    deactivatedReason: string;
  }>;
}

export type ReviewAgentEvent =
  | StatusChangeEvent
  | NodeReviewProgressEvent
  | TokenStreamEvent
  | IterationCompleteEvent
  | CleanupProgressEvent;

// ========== State Types ==========

export interface ReviewAgentState {
  status: ReviewAgentStatus;
  settings: ReviewAgentSettings;
  currentIteration: ReviewIteration | null;
  iterations: ReviewIterationSummary[];
  isConnected: boolean;
  lastError: string | null;
}

// ========== Agent Display Types ==========

export interface ReviewAgentCard {
  agentId: string;
  agentRole: AgentRole;
  tokens: string;
  isComplete: boolean;
}
