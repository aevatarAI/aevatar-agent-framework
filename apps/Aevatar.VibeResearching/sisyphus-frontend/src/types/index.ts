// ============================================================
//  Sisyphus Types - Core Domain Models
// ============================================================

// === Session Types ===
export interface SisyphusSession {
  id: string
  status: "pending" | "running" | "completed" | "failed"
  phase: string
  progressPercent: number
  totalTokens: number
  totalLlmCalls: number
  createdAt?: string  // ISO timestamp
}

// === Node Status ===
export type NodeStatus = 
  | "waiting" 
  | "running" 
  | "completed" 
  | "blocked" 
  | "scraping" 
  | "error"

// === Node Kind (FR-007/008) ===
export type NodeKind = "Plan" | "Knowledge"

// === Plan Node Status (FR-007) ===
export type PlanNodeStatus = "Pending" | "Active" | "Completed"

// === Workflow DAG Types ===
export interface DAGNode {
  id: string
  label: string
  type: string  // Generic | MathAxiom | MathTheorem | MathLemma | BiologyExperiment | CodeModule
  status: string  // pending | running | completed | error
  description?: string
  progress?: number
  position?: { x: number; y: number }
  // Extended fields from API
  kind?: NodeKind
  owner?: string
  proof?: string
  attestations?: { pubkey?: string; signature?: string }[]
  attestationsCount?: number
  // FR-007 Plan Node fields
  planStatus?: PlanNodeStatus
  methodology?: string
  sequentialOrder?: number
  progressText?: string
  // FR-008 Knowledge Node fields
  derivationProcess?: string
  references?: string[]
  // Cross-session identification
  sessionId?: string
}

export interface DAGEdge {
  source: string
  target: string
  type?: string  // "depends_on" | "motivated_by" | etc.
}

export interface DAGGraph {
  nodes: DAGNode[]
  edges: DAGEdge[]
}

// === Worker Agent Types ===
export interface WorkerAgent {
  id: string
  name: string
  status: "pending" | "running" | "streaming" | "completed" | "error"
  streaming: boolean
  streamContent: string
  lastResponse: string
  tokenIndex: number
  stepId?: string
  stepType?: string
  errorMessage?: string
  provider?: string
  history: WorkerHistoryItem[]
}

export interface WorkerHistoryItem {
  timestamp: number
  stepId: string
  phase: string
  status: string
  system?: string
  user?: string
  response?: string
}

// === Chat Message Types ===
export interface ChatMessage {
  id: string
  role: "user" | "agent" | "system"
  content: string
  timestamp: number
  agentId?: string
  agentName?: string
}

// === Research Brief ===
export interface ResearchBrief {
  id: string
  title: string
  summary: string
  keywords: string[]
  sources: string[]
}

// === System Stats ===
export interface SystemStats {
  computeLoad: number
  networkIO: number
  latency: number
  memoryUsage: number
}

// === AG-UI Event Types ===
export type AgUiEvent =
  | ProgressEvent
  | GraphEvent
  | ResultEvent
  | ErrorEvent
  | TextMessageStartEvent
  | TextMessageContentEvent
  | TextMessageEndEvent
  | StateSnapshotEvent
  | StateDeltaEvent
  | CustomEvent

export interface ProgressEvent {
  type: "ProgressEvent"
  sessionId: string
  phase: string
  progressPercent: number
  totalTokens: number
  totalLlmCalls: number
  workerId?: string
  stepId?: string
  stepType?: string
  stepStatus?: string
  providerName?: string
  tokenIndex?: number
  error?: string
  message?: string
}

export interface GraphEvent {
  type: "GraphEvent"
  sessionId: string
  iteration: number
  axioms: string[]
  assumptions: unknown[]
  theorems: unknown[]
}

export interface ResultEvent {
  type: "ResultEvent"
  sessionId: string
  success: boolean
  content?: string
  error?: string
}

export interface ErrorEvent {
  type: "ErrorEvent"
  sessionId: string
  message: string
  error?: string
}

export interface TextMessageStartEvent {
  type: "TEXT_MESSAGE_START"
  sessionId: string
  messageId: string
}

export interface TextMessageContentEvent {
  type: "TEXT_MESSAGE_CONTENT"
  sessionId: string
  messageId: string
  delta: string
}

export interface TextMessageEndEvent {
  type: "TEXT_MESSAGE_END"
  sessionId: string
  messageId: string
}

export interface StateSnapshotEvent {
  type: "STATE_SNAPSHOT"
  sessionId: string
  snapshot: unknown
}

export interface StateDeltaEvent {
  type: "STATE_DELTA"
  sessionId: string
  delta: unknown[]
}

export interface CustomEvent {
  type: "CUSTOM"
  name: string
  value: unknown
}

// === Settings Types ===
export interface ApiInfo {
  version?: string
  llm?: {
    default?: string
    providers?: string[]
    providersAll?: string[]
  }
}

export interface LlmProvider {
  id: string
  displayName: string
  description?: string
  category: "configured" | "popular" | "other"
  apiKeyConfigured?: boolean
  recommended?: boolean
}

// Provider public details (from GET /api/llm/provider/:name)
export interface ProviderPublic {
  providerName: string
  displayName: string
  kind: string
  apiKeyConfigured: boolean
  endpoint: string
  endpointSource: "secret" | "default" | "missing"
  model: string
  modelSource: "secret" | "default" | "missing"
}

export interface AgentProvider {
  agent: string
  providerName: string
}

// === Tools Types ===
export interface ToolSummary {
  name: string
  description?: string
  category?: string
  source?: string // 'MCP' | 'AGENT_SKILLS' | etc.
  tags?: string[]
}

// === Tool Output Types (for chat messages) ===
export interface ToolOutput {
  toolCallId: string
  name: string
  status: "running" | "done"
  isMcp?: boolean
  success?: boolean
  durationMs?: number
  error?: string
  resultPreview?: string
}

// === Extended Chat Message with Tool Outputs ===
export interface ChatMessageWithTools extends ChatMessage {
  toolOutputs?: ToolOutput[]
  isFinal?: boolean
}

// === SkillsMP Types ===
export interface SkillsMpItem {
  id?: string
  name?: string
  description?: string
  repoUrl?: string
  url?: string
  stars?: number
}

export interface SkillPackConfig {
  name?: string
  repoUrl: string
  ref?: string
  skillsSubDir?: string
  sync?: boolean
}

// ============================================================
//  Event Inspector Types - Workflow Event Visualization
// ============================================================

// === Event Categories ===
export type EventCategory = 
  | 'consensus'    // Vote consensus events
  | 'proposal'     // Proposal generation
  | 'vote'         // Individual votes
  | 'tool_call'    // Tool executions
  | 'red_flag'     // Red-flag validation failures
  | 'parallel'     // Parallel execution progress
  | 'llm'          // LLM request/response

// === Workflow Step Event (from backend) ===
export interface WorkflowStepFields {
  // Core
  status?: string
  progress?: number
  execution_id?: string
  workflow_name?: string
  step_type?: string
  depth?: number
  parent_step_id?: string
  // Voting
  vote_round?: number
  vote_max_rounds?: number
  vote_k?: number
  vote_current_votes?: number
  // LLM Conversation
  system_prompt?: string
  user_prompt?: string
  assistant_response?: string
  // Red-Flag
  red_flag_reason?: string
  // Winner Info
  winner_proposal_id?: string
  winner_hash?: string
  winner_votes?: number
  winner_runner_up_votes?: number
  winner_semantic?: boolean
  winner_cluster_count?: number
  winner_is_consensus?: boolean
  // Parallel
  parallel_total?: number
  parallel_completed?: number
  parallel_failed?: number
  // Tool
  tool_name?: string
  tool_call_id?: string
  duration_ms?: number
  error?: string
  // Tokens
  tokens_used?: number
  llm_calls?: number
}

export interface WorkflowExecutionEvent {
  phase: string
  nodeId: string
  message: string
  status: string
  timestamp: number
  fields: WorkflowStepFields
}

// === Classified Event (for UI display) ===
export interface ClassifiedEvent {
  id: string
  timestamp: number
  category: EventCategory
  title: string
  message: string
  status: 'pending' | 'running' | 'completed' | 'failed'
  // Raw data
  raw: WorkflowExecutionEvent
  // Optional expanded data
  voteInfo?: VoteInfo
  toolInfo?: ToolCallInfo
  redFlagInfo?: RedFlagInfo
  llmConversation?: LlmConversation
}

// === Vote Information ===
export interface VoteInfo {
  round: number
  maxRounds: number
  k: number
  currentVotes: number
  mode: 'semantic' | 'hash'
  similarityThreshold?: number
  redFlagCount?: number
  clusterCount?: number
}

// === Vote Winner Information ===
export interface VoteWinnerInfo {
  proposalId: string
  hash: string
  votes: number
  runnerUpVotes: number
  mode: 'semantic' | 'hash'
  isConsensus: boolean
  clusterCount: number
}

// === Tool Call Information ===
export interface ToolCallInfo {
  toolName: string
  toolCallId: string
  status: 'pending' | 'success' | 'error'
  durationMs?: number
  error?: string
}

// === Red Flag Information ===
export interface RedFlagInfo {
  reason: string
  round: number
  proposalIndex?: number
}

// === LLM Conversation ===
export interface LlmConversation {
  systemPrompt?: string
  userPrompt?: string
  assistantResponse?: string
}

// === Aggregated Events (for timeline grouping) ===
export interface AggregatedEvent {
  id: string
  category: EventCategory
  count: number
  firstTimestamp: number
  lastTimestamp: number
  events: ClassifiedEvent[]
  expanded: boolean
}

// === Flow Graph State ===
export interface FlowGraphState {
  visibleLayers: {
    maker: boolean    // Coordinator + Workers + Vote edges
    vibe: boolean     // Planner + Reasoner + Librarian
    tool: boolean     // Tools + Tool edges
  }
  detailMode: boolean
  focusMode: boolean
  focusedNodeId: string | null
}

// === Voting Status (for panel display) ===
export interface VotingStatus {
  round: number
  maxRounds: number
  k: number
  mode: 'semantic' | 'hash'
  similarityThreshold?: number
  redFlagCount: number
  clusterCount: number
  workers: WorkerVoteInfo[]
  consensusReached: boolean
  winner?: VoteWinnerInfo
}

export interface WorkerVoteInfo {
  id: string
  name: string
  votes: number
  color: string
  isLeader: boolean
}

// === Event Inspector Store Slice ===
export interface EventInspectorState {
  events: ClassifiedEvent[]
  aggregatedEvents: AggregatedEvent[]
  activeTab: 'timeline' | 'flow-graph'
  filter: {
    categories: EventCategory[]
    search: string
    timeRange: [number, number] | null
  }
  flowGraph: FlowGraphState
  votingStatus: VotingStatus | null
}
