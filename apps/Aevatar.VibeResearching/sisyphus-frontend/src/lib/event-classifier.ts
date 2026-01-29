// ============================================================
//  Event Classifier - Transform workflow events to UI events
// ============================================================

import type {
  ClassifiedEvent,
  EventCategory,
  WorkflowExecutionEvent,
  WorkflowStepFields,
  VoteInfo,
  ToolCallInfo,
  RedFlagInfo,
  LlmConversation,
} from '@/types'

/**
 * Classify a raw workflow event into a UI-friendly ClassifiedEvent
 */
export function classifyEvent(raw: WorkflowExecutionEvent): ClassifiedEvent {
  const { phase, nodeId, message, status, timestamp, fields } = raw
  const category = detectCategory(phase, fields)
  
  return {
    id: `${nodeId}-${timestamp}`,
    timestamp,
    category,
    title: buildTitle(category, phase, fields),
    message,
    status: mapStatus(status),
    raw,
    voteInfo: extractVoteInfo(fields),
    toolInfo: extractToolInfo(fields),
    redFlagInfo: extractRedFlagInfo(fields),
    llmConversation: extractLlmConversation(fields),
  }
}

/**
 * Detect event category from phase and fields
 */
function detectCategory(phase: string, fields: WorkflowStepFields): EventCategory {
  // Red flag events
  if (fields.red_flag_reason) {
    return 'red_flag'
  }
  
  // Tool call events
  if (fields.tool_name || fields.tool_call_id) {
    return 'tool_call'
  }
  
  // Voting/consensus events
  if (fields.winner_proposal_id || fields.winner_is_consensus) {
    return 'consensus'
  }
  
  if (fields.vote_round !== undefined || fields.vote_current_votes !== undefined) {
    return 'vote'
  }
  
  // Parallel execution events
  if (fields.parallel_total !== undefined && fields.parallel_total > 0) {
    return 'parallel'
  }
  
  // LLM conversation events
  if (fields.assistant_response || fields.user_prompt) {
    return 'llm'
  }
  
  // Phase-based detection
  const phaseLower = phase.toLowerCase()
  if (phaseLower.includes('vote') || phaseLower.includes('consensus')) {
    return 'vote'
  }
  if (phaseLower.includes('proposal') || phaseLower.includes('generate')) {
    return 'proposal'
  }
  
  return 'proposal' // default
}

/**
 * Build a human-readable title
 */
function buildTitle(category: EventCategory, phase: string, fields: WorkflowStepFields): string {
  switch (category) {
    case 'consensus':
      return `Consensus Reached (${fields.winner_cluster_count || 1} clusters)`
    case 'vote':
      return `Vote Round ${fields.vote_round || '?'}/${fields.vote_max_rounds || '?'}`
    case 'red_flag':
      return 'Red Flag Validation'
    case 'tool_call':
      return `Tool: ${fields.tool_name || 'Unknown'}`
    case 'parallel':
      return `Parallel Progress: ${fields.parallel_completed || 0}/${fields.parallel_total || 0}`
    case 'llm':
      return 'LLM Response'
    case 'proposal':
    default:
      return phase || 'Workflow Step'
  }
}

/**
 * Map backend status to UI status
 */
function mapStatus(status: string): 'pending' | 'running' | 'completed' | 'failed' {
  const statusLower = status.toLowerCase()
  if (statusLower.includes('fail') || statusLower.includes('error')) {
    return 'failed'
  }
  if (statusLower.includes('complet') || statusLower.includes('done') || statusLower.includes('success')) {
    return 'completed'
  }
  if (statusLower.includes('run') || statusLower.includes('progress') || statusLower.includes('active')) {
    return 'running'
  }
  return 'pending'
}

/**
 * Extract vote information from fields
 */
function extractVoteInfo(fields: WorkflowStepFields): VoteInfo | undefined {
  if (fields.vote_round === undefined && fields.vote_current_votes === undefined) {
    return undefined
  }
  
  return {
    round: fields.vote_round || 0,
    maxRounds: fields.vote_max_rounds || 10,
    k: fields.vote_k || 3,
    currentVotes: fields.vote_current_votes || 0,
    mode: fields.winner_semantic ? 'semantic' : 'hash',
    clusterCount: fields.winner_cluster_count,
  }
}

/**
 * Extract tool call information from fields
 */
function extractToolInfo(fields: WorkflowStepFields): ToolCallInfo | undefined {
  if (!fields.tool_name && !fields.tool_call_id) {
    return undefined
  }
  
  let status: 'pending' | 'success' | 'error' = 'pending'
  if (fields.error) {
    status = 'error'
  } else if (fields.duration_ms !== undefined) {
    status = 'success'
  }
  
  return {
    toolName: fields.tool_name || 'unknown',
    toolCallId: fields.tool_call_id || '',
    status,
    durationMs: fields.duration_ms,
    error: fields.error,
  }
}

/**
 * Extract red flag information from fields
 */
function extractRedFlagInfo(fields: WorkflowStepFields): RedFlagInfo | undefined {
  if (!fields.red_flag_reason) {
    return undefined
  }
  
  return {
    reason: fields.red_flag_reason,
    round: fields.vote_round || 0,
  }
}

/**
 * Extract LLM conversation from fields
 */
function extractLlmConversation(fields: WorkflowStepFields): LlmConversation | undefined {
  if (!fields.system_prompt && !fields.user_prompt && !fields.assistant_response) {
    return undefined
  }
  
  return {
    systemPrompt: fields.system_prompt,
    userPrompt: fields.user_prompt,
    assistantResponse: fields.assistant_response,
  }
}

/**
 * Parse a CustomEvent from SSE into WorkflowExecutionEvent
 */
export function parseCustomEvent(customEvent: {
  name: string
  value: unknown
}): WorkflowExecutionEvent | null {
  if (customEvent.name !== 'aevatar.workflow.execution_event') {
    return null
  }
  
  const value = customEvent.value as Record<string, unknown>
  const fields = (value.fields || {}) as WorkflowStepFields
  
  return {
    phase: (value.phase as string) || '',
    nodeId: (value.nodeId as string) || (value.node_id as string) || '',
    message: (value.message as string) || '',
    status: (value.status as string) || 'pending',
    timestamp: (value.timestamp as number) || Date.now(),
    fields,
  }
}
