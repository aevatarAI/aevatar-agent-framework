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

// === Workflow DAG Types ===
export interface DAGNode {
  id: string
  label: string
  type: string  // axiom | theorem | lemma | hypothesis | task | agent
  status: string  // pending | running | completed | error
  description?: string
  progress?: number
  position?: { x: number; y: number }
  // Extended fields from API
  kind?: string
  owner?: string
  proof?: string
  attestations?: { pubkey?: string; signature?: string }[]
  attestationsCount?: number
}

export interface DAGEdge {
  source: string
  target: string
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
