// ============================================================================
//  Axiom Client - Type Definitions
// ============================================================================

// === Session Types ===

export interface AxiomSession {
  sessionId: string
  id?: string
  status?: string | number
  phase?: string
  progressPercent?: number
  totalTokens?: number
  totalLlmCalls?: number
  createdAt?: string | { seconds?: number; nanos?: number }
  providerName?: string | null
  ownerId?: string
  ownerName?: string
}

export interface CreateSessionPayload {
  axioms: string
  goal?: string
  seedHypothesis?: string
  workflow?: string
  language?: string
  k?: number
  maxRounds?: number
  maxDepth?: number
  maxDurationMinutes?: number
  maxLlmCalls?: number
  maxTokens?: number
  continueOnFailure?: boolean
}

export interface RunResult {
  success: boolean
  sessionId?: string
  error?: string
}

// === Agent Types ===

export interface ToolCallInfo {
  id: string
  toolName: string
  arguments?: string
  type?: string
}

export interface ToolResultInfo {
  toolCallId: string
  toolName: string
  content?: string
  isSuccess: boolean
}

export interface AgentChatMessage {
  id: string
  role: 'user' | 'assistant' | 'system' | 'tool'
  content: string
  toolCalls?: ToolCallInfo[]
  toolResult?: ToolResultInfo
  timestamp?: string
  tokenUsed: number
  metadata?: Record<string, string>
}

export interface AgentState {
  history: AgentChatMessage[]
  totalTokenUsed: number
  lastActivity: string | null
  context: Record<string, string>
}

export interface AgentStateBundle {
  agentId: string
  state: AgentState
}

export interface SessionAgentsInfo {
  sessionId: string
  coordinatorId: string
  workerIds: string[]
  agentIds: string[]
}

// === Session Status Types ===

export interface SessionStatusAgent {
  agent: string
  stepName: string
  providerName: string
  status: 'running' | 'idle'
}

export interface SessionStatusStep {
  status?: 'running' | 'done' | 'pending'
  startedAt?: string
  finishedAt?: string
}

export interface SessionStatus {
  ok: boolean
  sessionId: string
  runId: string
  updatedAt: string
  steps: {
    order: string[]
    map: Record<string, SessionStatusStep>
    running: string[]
    done: string[]
  }
  agents: SessionStatusAgent[]
  runningTools: Array<{
    messageId: string
    toolCallId: string
    toolName: string
    status: string
    startedAt: string
    providerName: string
    targetAgent: string
  }>
}

// === DAG Types ===

export interface DagNode {
  id: string
  type?: string
  kind?: string
  label?: string
  proof?: string
  owner?: string
  attestationsCount?: number
  attestations?: Array<{ pubkey?: string; signature?: string }>
  updatedAt?: string
  tags?: Record<string, string>
  sessionId?: string
  planStatus?: string
}

export interface DagEdge {
  fromId: string
  toId: string
  type?: string
}

export interface DagSnapshot {
  sessionId?: string
  updatedAt?: string
  nodes?: DagNode[]
  edges?: DagEdge[]
  truncated?: boolean
}

export interface DagNodeExplain {
  provable?: boolean
  hasCycle?: boolean
  directDeps?: string[]
  missing?: Array<{ id: string; type?: string }>
}

// === Message Types ===

export interface SendMessagePayload {
  text: string
  mode?: 'chat' | 'vibe' | 'vibe_loop'
  toAgents?: string[]
  attachmentPaths?: string[]
}

// === Worker Types ===

export interface ParsedHistoryItem {
  stepId: string
  timestamp: number
  phase: string
  status: string
  system?: string
  user?: string
  response?: string
}

export interface ParsedWorker {
  id: string
  name: string
  status: 'pending' | 'running' | 'completed' | 'error'
  provider?: string
  stepId?: string
  stepType?: string
  tokenIndex?: number
  lastResponse?: string
  history: ParsedHistoryItem[]
}

// === LLM Provider Types ===

export interface ProviderItem {
  id: string
  displayName: string
  category: 'configured' | 'popular' | 'other'
  description?: string
  recommended?: boolean
  apiKeyConfigured?: boolean
}

export interface ProviderPublic {
  providerName: string
  displayName: string
  kind: string
  apiKeyConfigured: boolean
  endpoint: string
  endpointSource: 'secret' | 'default' | 'missing'
  model: string
  modelSource: 'secret' | 'default' | 'missing'
}

export interface ProviderInstance {
  name: string
  providerType: string
  providerDisplayName: string
  model: string
  endpoint: string
}

export interface ApiKeyStatusResponse {
  ok: boolean
  configured: boolean
  masked: string
  value?: string
}

export interface TestProviderResponse {
  ok: boolean
  error?: string
  latencyMs?: number
  modelsCount?: number
  message?: string
}

export interface FetchModelsResponse {
  ok: boolean
  models: string[]
}

// === Tools Types ===

export interface ToolSummary {
  name: string
  description?: string
  category?: string
  source?: string
  tags?: string[]
}

// === SkillsMP Types ===

export interface SkillsMpStatus {
  ok: boolean
  configured: boolean
  masked: string
  keyPath?: string
}

export interface SkillsMpItem {
  id?: string
  name?: string
  description?: string
  repoUrl?: string
  url?: string
  stars?: number
}

export interface SkillsMpSearchResponse {
  ok: boolean
  items: SkillsMpItem[]
  total?: number
}

export interface SkillPackConfig {
  name?: string
  repoUrl: string
  ref?: string
  skillsSubDir?: string
  sync?: boolean
}

// === Knowledge Graph Types ===

export interface NodeExplanation {
  nodeId: string
  title: string
  kind: 'Plan' | 'Knowledge'
  markdownContent: string
  directDependencies: string[]
  fullChainNodeIds: string[]
  dependents: string[]
}

export interface SessionSummary {
  sessionId: string
  status: string
  markdownContent: string
  planNodeCount: number
  knowledgeNodeCount: number
  progressPercentage: number
}

export interface DagSummary {
  sessionId: string
  markdownContent: string
  totalNodes: number
  totalEdges: number
  maxDepth: number
}

export interface PivotSnapshot {
  id: string
  createdAt: string
  reason: string
  nodeCount: number
  edgeCount: number
}

// === Upload Types ===

export interface ExtractedKnowledgeNode {
  id: string
  title: string
  content: string
  keywords: string[]
}

export interface UploadExtractionResponse {
  ok: boolean
  sessionId?: string
  fileName?: string
  filePath?: string
  message?: string
  error?: string
  extractedNodes?: ExtractedKnowledgeNode[]
}

// === User-Level LLM Provider Types (re-export from dedicated module) ===
export type {
  UserLlmProviderDto,
  UserLlmProviderListResponse,
  CreateUserProviderRequest,
  UpdateUserProviderRequest,
  SetDefaultProviderRequest,
  ProviderTestResult,
  ProviderModelItem,
  ProviderModelsResponse,
  CodexInitiateRequest,
  CodexInitiateResponse,
  CodexCallbackRequest,
  CodexCallbackResponse,
  CodexStatusResponse,
  AgentProviderDetail,
  AgentProvidersSnapshotResponse,
  UpdateAgentProvidersRequest,
  AvailableProviderDto,
  AvailableProvidersResponse,
  OkResponse,
  ApiErrorResponse,
  ProviderSource,
  SupportedProviderType,
} from '@/types/user-provider'
