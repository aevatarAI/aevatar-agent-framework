// ============================================================================
//  AxiomReasoning API Client
//  Uses @aevatar/kit-core + @aevatar/kit-protocol for AG-UI integration
// ============================================================================

import { createEventStream, type EventStream, type AevatarProgressEvent } from "@aevatar/kit-protocol"

// === API Types ===
export interface AxiomSession {
  sessionId: string  // Backend returns sessionId, not id
  id?: string        // Mapped field for internal use
  status?: string
  phase?: string
  progressPercent?: number
  totalTokens?: number
  totalLlmCalls?: number
  createdAt?: string
  providerName?: string | null
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

// === Agent State Types (from Session API) ===

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
  role: "user" | "assistant" | "system" | "tool"
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

// === API Base URL ===
// Development: uses Vite proxy (see vite.config.ts → localhost:5678)
// Production: set VITE_AXIOM_API_BASE to full backend URL
const API_BASE = import.meta.env.VITE_AXIOM_API_BASE || ""

// === Request Caching & Deduplication ===
interface CacheEntry<T> {
  data: T
  timestamp: number
}

interface PendingRequest<T> {
  promise: Promise<T>
  abortController: AbortController
}

// Cache with TTL (5 seconds default)
const requestCache = new Map<string, CacheEntry<unknown>>()
const CACHE_TTL_MS = 5000

// Pending requests for deduplication
const pendingRequests = new Map<string, PendingRequest<unknown>>()

// Current session abort controller - for cancelling all requests when switching sessions
let currentSessionAbortController: AbortController | null = null

/**
 * Get or create AbortController for the current session
 */
export function getSessionAbortController(): AbortController {
  if (!currentSessionAbortController) {
    currentSessionAbortController = new AbortController()
  }
  return currentSessionAbortController
}

/**
 * Abort all pending requests for the current session and create a new controller
 */
export function abortCurrentSessionRequests(): void {
  if (currentSessionAbortController) {
    currentSessionAbortController.abort()
  }
  currentSessionAbortController = new AbortController()
  // Clear pending requests map since they're all aborted
  pendingRequests.clear()
}

/**
 * Clear the request cache (useful when data may have changed)
 */
export function clearRequestCache(): void {
  requestCache.clear()
}

// === Fetch Helper ===
async function fetchJson<T>(
  path: string,
  init?: RequestInit,
  options?: { cache?: boolean; cacheTtl?: number }
): Promise<T> {
  const cacheKey = `${init?.method || 'GET'}:${path}`
  const useCaching = options?.cache !== false && (!init?.method || init.method === 'GET')
  const ttl = options?.cacheTtl ?? CACHE_TTL_MS

  // Check cache first
  if (useCaching) {
    const cached = requestCache.get(cacheKey) as CacheEntry<T> | undefined
    if (cached && Date.now() - cached.timestamp < ttl) {
      return cached.data
    }
  }

  // Check for pending identical request (deduplication)
  if (useCaching && pendingRequests.has(cacheKey)) {
    return pendingRequests.get(cacheKey)!.promise as Promise<T>
  }

  // Create abort controller linked to session controller
  const abortController = new AbortController()
  const sessionController = getSessionAbortController()

  // Link to session abort
  const abortHandler = () => abortController.abort()
  sessionController.signal.addEventListener('abort', abortHandler)

  const fetchPromise = (async () => {
    try {
      const res = await fetch(`${API_BASE}${path}`, {
        ...init,
        signal: abortController.signal,
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
          ...init?.headers,
        },
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(`API Error ${res.status}: ${text}`)
      }
      const data = await res.json() as T

      // Store in cache
      if (useCaching) {
        requestCache.set(cacheKey, { data, timestamp: Date.now() })
      }

      return data
    } finally {
      sessionController.signal.removeEventListener('abort', abortHandler)
      pendingRequests.delete(cacheKey)
    }
  })()

  // Store pending request for deduplication
  if (useCaching) {
    pendingRequests.set(cacheKey, { promise: fetchPromise, abortController })
  }

  return fetchPromise
}

// === API Functions ===

/**
 * List all sessions
 */
export async function listSessions(): Promise<AxiomSession[]> {
  const data = await fetchJson<{ count: number; sessions: AxiomSession[] }>("/api/sessions")
  return data?.sessions || []
}

/**
 * Get available workflows
 */
export async function listWorkflows(): Promise<string[]> {
  const data = await fetchJson<string[]>("/api/workflows")
  return data || ["hypothesis_promotion_loop"]
}

/**
 * Create a new session (simple)
 */
export async function createSession(providerName?: string): Promise<{ ok: boolean; sessionId?: string; error?: string }> {
  return fetchJson<{ ok: boolean; sessionId?: string; error?: string }>("/api/sessions", {
    method: "POST",
    body: JSON.stringify(providerName ? { providerName } : {}),
  })
}

/**
 * Start/Run a session
 */
export async function runSession(sessionId: string | null | undefined): Promise<RunResult> {
  if (!sessionId) {
    console.warn('[axiom-client] runSession called with invalid sessionId:', sessionId);
    return { success: false, error: 'Invalid sessionId' };
  }
  return fetchJson<RunResult>(`/api/sessions/${sessionId}/run`, {
    method: "POST",
  })
}

/**
 * Send message payload for chat/vibe modes
 * Matches SraSendInputRequest from original frontend
 */
export interface SendMessagePayload {
  text: string
  mode?: "chat" | "vibe"
  toAgents?: string[]
  attachmentPaths?: string[]
}

/**
 * Send a message to the session (chat/vibe)
 * Uses /api/sessions/:id/input endpoint
 */
export async function sendMessage(
  sessionId: string | null | undefined,
  payload: SendMessagePayload
): Promise<{ ok: boolean; runId?: string; error?: string }> {
  if (!sessionId) {
    console.warn('[axiom-client] sendMessage called with invalid sessionId:', sessionId);
    return { ok: false, error: 'Invalid sessionId' };
  }
  
  // Single endpoint for all modes: /api/sessions/:id/input
  const endpoint = `/api/sessions/${encodeURIComponent(sessionId)}/input`
  
  // Build request body matching SraSendInputRequest
  const body: Record<string, unknown> = {
    message: payload.text,
    mode: payload.mode || "vibe",
  }
  
  // Handle agent targeting
  if (payload.toAgents && payload.toAgents.length > 0) {
    body.toAgents = payload.toAgents
  }
  
  // Handle attachments
  if (payload.attachmentPaths && payload.attachmentPaths.length > 0) {
    body.attachmentPaths = payload.attachmentPaths
  }
  
  return fetchJson<{ ok: boolean; runId?: string; error?: string }>(endpoint, {
    method: "POST",
    body: JSON.stringify(body),
  })
}

/**
 * Stop a session
 */
export async function stopSession(sessionId: string | null | undefined): Promise<RunResult> {
  if (!sessionId) {
    console.warn('[axiom-client] stopSession called with invalid sessionId:', sessionId);
    return { success: false, error: 'Invalid sessionId' };
  }
  return fetchJson<RunResult>(`/api/sessions/${sessionId}/stop`, {
    method: "POST",
  })
}

/**
 * Get session result
 */
export async function getSessionResult(sessionId: string | null | undefined): Promise<unknown> {
  if (!sessionId) {
    console.warn('[axiom-client] getSessionResult called with invalid sessionId:', sessionId);
    return null;
  }
  return fetchJson<unknown>(`/api/sessions/${sessionId}/result`)
}

/**
 * Get session agents list (coordinator + workers)
 */
export async function getSessionAgents(sessionId: string | null | undefined): Promise<SessionAgentsInfo | null> {
  if (!sessionId) {
    console.warn('[axiom-client] getSessionAgents called with invalid sessionId:', sessionId);
    return null;
  }
  return fetchJson<SessionAgentsInfo>(`/api/sessions/${sessionId}/agents`)
}

/**
 * Get all agent states for a session (includes history and token usage)
 * @param sessionId - Session ID
 * @param includeHistory - Whether to include chat history (default: true)
 * @param historyLimit - Max history entries per agent (default: 50)
 */
export async function getAgentStates(
  sessionId: string | null | undefined,
  includeHistory = true,
  historyLimit = 50
): Promise<AgentStateBundle[]> {
  if (!sessionId) {
    console.warn('[axiom-client] getAgentStates called with invalid sessionId:', sessionId);
    return [];
  }
  const params = new URLSearchParams({
    include_history: String(includeHistory),
    history_limit: String(historyLimit),
  });
  const result = await fetchJson<{ agents?: AgentStateBundle[] }>(
    `/api/sessions/${sessionId}/agents/states?${params}`,
    undefined,
    { cache: false }  // Disable cache for real-time data
  );
  return result?.agents || [];
}

/**
 * Get single agent history
 */
export async function getAgentHistory(
  sessionId: string | null | undefined,
  agentId: string,
  limit = 50
): Promise<AgentChatMessage[]> {
  if (!sessionId || !agentId) {
    console.warn('[axiom-client] getAgentHistory called with invalid params:', { sessionId, agentId });
    return [];
  }
  const result = await fetchJson<{ history?: AgentChatMessage[] }>(
    `/api/sessions/${sessionId}/agents/${encodeURIComponent(agentId)}/history?limit=${limit}`,
    undefined,
    { cache: false }
  );
  return result?.history || [];
}

// === Session Status Types ===

export interface SessionStatusAgent {
  agent: string
  stepName: string
  providerName: string
  status: "running" | "idle"
}

export interface SessionStatusStep {
  status?: "running" | "done" | "pending"
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

/**
 * Get session status including running agents, steps, and tools.
 * Used to detect if a session has an active run on page load.
 */
export async function getSessionStatus(
  sessionId: string | null | undefined
): Promise<SessionStatus | null> {
  if (!sessionId) {
    console.warn('[axiom-client] getSessionStatus called with invalid sessionId:', sessionId);
    return null;
  }
  const result = await fetchJson<SessionStatus>(
    `/api/sessions/${sessionId}/status`,
    undefined,
    { cache: false }
  );
  return result ?? null;
}

/**
 * Get DAG snapshot
 */
export async function getDagSnapshot(sessionId: string | null | undefined): Promise<DagSnapshot | null> {
  if (!sessionId) {
    console.warn('[axiom-client] getDagSnapshot called with invalid sessionId:', sessionId);
    return null;
  }
  const result = await fetchJson<{ dag?: DagSnapshot }>(`/api/sessions/${sessionId}/dag`)
  return result?.dag ?? null
}

/**
 * Get Global DAG snapshot (all nodes across all sessions)
 * No sessionId required - returns the complete knowledge graph
 */
export async function getGlobalDagSnapshot(): Promise<DagSnapshot | null> {
  const result = await fetchJson<{ dag?: DagSnapshot }>("/api/dag/global")
  return result?.dag ?? null
}

/**
 * DAG Node type
 */
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
  sessionId?: string  // Source session ID for cross-session rendering
  planStatus?: string  // Plan node execution status: "Pending" | "Active" | "Completed"
}

/**
 * DAG Edge type
 */
export interface DagEdge {
  fromId: string
  toId: string
  type?: string
}

/**
 * DAG Snapshot type
 */
export interface DagSnapshot {
  sessionId?: string
  updatedAt?: string
  nodes?: DagNode[]
  edges?: DagEdge[]
  truncated?: boolean
}

/**
 * DAG Node Explain result
 */
export interface DagNodeExplain {
  provable?: boolean
  hasCycle?: boolean
  directDeps?: string[]
  missing?: Array<{ id: string; type?: string }>
}

/**
 * Get DAG node explanation (provability, dependencies, etc.)
 */
export async function getDagNodeExplain(
  sessionId: string | null | undefined,
  nodeId: string
): Promise<DagNodeExplain | null> {
  if (!sessionId || !nodeId) {
    console.warn('[axiom-client] getDagNodeExplain called with invalid params:', { sessionId, nodeId });
    return null;
  }
  const result = await fetchJson<{ explain?: DagNodeExplain }>(
    `/api/sessions/${encodeURIComponent(sessionId)}/dag/${encodeURIComponent(nodeId)}/explain`
  )
  return result?.explain ?? null
}

/**
 * Get Knowledge Graph chain markdown for a node
 */
export async function getKnowledgeChain(
  sessionId: string | null | undefined,
  nodeId: string
): Promise<string> {
  if (!sessionId || !nodeId) {
    console.warn('[axiom-client] getKnowledgeChain called with invalid params:', { sessionId, nodeId });
    return '';
  }
  try {
    const result = await fetchJson<{ markdown?: string }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/${encodeURIComponent(nodeId)}/chain`
    )
    return result?.markdown ?? ''
  } catch {
    // Best-effort, may not be available
    return ''
  }
}

/**
 * Get session events history (for reconstructing workers from completed sessions)
 */
export async function getSessionEvents(sessionId: string | null | undefined): Promise<string> {
  if (!sessionId) {
    console.warn('[axiom-client] getSessionEvents called with invalid sessionId:', sessionId);
    return '';
  }
  const sessionController = getSessionAbortController()
  const res = await fetch(`${API_BASE}/api/sessions/${sessionId}/agui/events`, {
    headers: { Accept: "text/event-stream" },
    signal: sessionController.signal,
  })
  if (!res.ok) {
    throw new Error(`Failed to fetch events: ${res.statusText}`)
  }
  return res.text()
}

/**
 * Worker history item type for parsing
 */
interface ParsedHistoryItem {
  stepId: string
  timestamp: number
  phase: string
  status: string
  system?: string
  user?: string
  response?: string
}

/**
 * Parsed worker type with history
 */
export interface ParsedWorker {
  id: string
  name: string
  status: "pending" | "running" | "completed" | "error"
  provider?: string
  stepId?: string
  stepType?: string
  tokenIndex?: number
  lastResponse?: string
  history: ParsedHistoryItem[]
}

/**
 * Parse workers from historical events (includes history)
 */
export function parseWorkersFromEvents(eventsText: string): Map<string, ParsedWorker> {
  const workers = new Map<string, ParsedWorker>()
  
  // Get worker name helper
  const getWorkerName = (id: string) => {
    const match = id.match(/worker-(\d+)/)
    if (match) return `Worker ${match[1]}`
    return id
  }
  
  // Parse SSE format: "data: {...json...}"
  const lines = eventsText.split('\n')
  for (const line of lines) {
    if (!line.startsWith('data: ')) continue
    try {
      const jsonStr = line.slice(6) // Remove "data: "
      const event = JSON.parse(jsonStr)
      
      // Only process ProgressEvent with workerId
      if (event.type !== 'ProgressEvent') continue
      if (!event.workerId || event.workerId === 'coordinator') continue
      
      const workerId = event.workerId as string
      const existing = workers.get(workerId) || {
        id: workerId,
        name: getWorkerName(workerId),
        status: "running" as const,
        history: [],
      }
      
      // Determine status
      let status: "pending" | "running" | "completed" | "error" = "running"
      if (event.stepStatus === 'Completed') status = "completed"
      else if (event.stepStatus === 'Failed') status = "error"
      
      // Extract history data from event
      const stepId = event.stepId || ''
      const systemPrompt = event.systemPrompt || ''
      const userPrompt = event.userPrompt || ''
      const tokenDelta = event.tokenDelta || ''
      const responseContent = event.assistantResponse || event.assistantResponsePreview || ''
      
      // Build/update history if we have stepId and content
      let newHistory = [...existing.history]
      if (stepId && (systemPrompt || userPrompt || tokenDelta || responseContent)) {
        const existingHistoryIdx = newHistory.findIndex(h => h.stepId === stepId)
        
        if (existingHistoryIdx >= 0) {
          // Update existing entry - accumulate response
          const existingItem = newHistory[existingHistoryIdx]
          newHistory[existingHistoryIdx] = {
            ...existingItem,
            system: existingItem.system || systemPrompt,
            user: existingItem.user || userPrompt,
            response: tokenDelta
              ? (existingItem.response || '') + tokenDelta
              : responseContent || existingItem.response,
          }
        } else {
          // Create new entry (prepend, keep last 12)
          newHistory = [
            {
              stepId,
              timestamp: event.timestamp || Date.now(),
              phase: event.phase || '',
              status: event.stepStatus || 'Running',
              system: systemPrompt || undefined,
              user: userPrompt || undefined,
              response: tokenDelta || responseContent || undefined,
            },
            ...newHistory.slice(0, 11),
          ]
        }
      }
      
      workers.set(workerId, {
        id: workerId,
        name: getWorkerName(workerId),
        status: status,
        provider: event.providerName || existing.provider,
        stepId: event.stepId || existing.stepId,
        stepType: event.stepType || existing.stepType,
        tokenIndex: event.tokenIndex ?? existing.tokenIndex,
        lastResponse: responseContent || event.message || existing.lastResponse,
        history: newHistory,
      })
    } catch {
      // Skip invalid JSON lines
    }
  }
  
  return workers
}

// === Event Stream Factory ===

/**
 * Custom event types for AxiomReasoning
 */
interface AxiomCustomEvents {
  [key: string]: unknown  // Index signature for CustomEventMap constraint
  "aevatar.progress": AevatarProgressEvent
  "aevatar.axiom.status_snapshot": {
    status: string
    phase: string
    progressPercent: number
    totalTokens: number
    totalLlmCalls: number
  }
  "aevatar.axiom.graph": {
    iteration: number
    axioms: unknown[]
    assumptions: unknown[]
    theorems: unknown[]
  }
}

/**
 * Create AG-UI event stream for a session
 * Note: onStatusChange should be registered by the caller (use-axiom-stream)
 * to properly update store state
 */
export function createAxiomEventStream(sessionId: string): EventStream<AxiomCustomEvents> {
  const url = `${API_BASE}/api/sessions/${sessionId}/agui/events`
  
  return createEventStream<AxiomCustomEvents>({
    url,
    autoReconnect: true,
    reconnectDelayMs: 2000,
    maxReconnectAttempts: 5,
    // Don't register onStatusChange here - let use-axiom-stream handle it
    // to properly update store.isConnected
    onError: (error, context) => {
      console.error(`[AxiomEventStream] Error:`, error, context)
    },
    onReconnecting: () => {
      // Silent reconnection
    },
    onReconnectFailed: () => {
      console.error(`[AxiomEventStream] All reconnection attempts failed`)
    },
  })
}

// ============================================================================
//  Settings & LLM API
// ============================================================================

/**
 * Provider item in list
 */
export interface ProviderItem {
  id: string
  displayName: string
  category: "configured" | "popular" | "other"
  description?: string
  recommended?: boolean
  apiKeyConfigured?: boolean
}

/**
 * Provider public details
 */
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

/**
 * API key status response
 */
export interface ApiKeyStatusResponse {
  ok: boolean
  configured: boolean
  masked: string
  value?: string  // Only returned when reveal=true
}

/**
 * Test provider response
 */
export interface TestProviderResponse {
  ok: boolean
  error?: string
  latencyMs?: number
  modelsCount?: number
  message?: string
}

/**
 * Fetch models response
 */
export interface FetchModelsResponse {
  ok: boolean
  models: string[]
}

/**
 * Get API info (version, LLM providers, etc.)
 */
export async function getApiInfo(): Promise<unknown> {
  return fetchJson<unknown>("/api/info")
}

/**
 * Get default LLM provider
 */
export async function getDefaultProvider(): Promise<{ ok: boolean; providerName: string }> {
  return fetchJson<{ ok: boolean; providerName: string }>("/api/llm/default")
}

/**
 * Set default LLM provider
 */
export async function setDefaultProvider(providerName: string): Promise<{ ok: boolean; providerName?: string; error?: string }> {
  return fetchJson<{ ok: boolean; providerName?: string; error?: string }>("/api/llm/default", {
    method: "POST",
    body: JSON.stringify({ providerName }),
  })
}

/**
 * List all LLM providers (provider types catalog)
 */
export async function listLlmProviders(): Promise<{ providers: ProviderItem[] }> {
  return fetchJson<{ providers: ProviderItem[] }>("/api/llm/providers")
}

/**
 * Provider instance (configured with API key)
 */
export interface ProviderInstance {
  name: string
  providerType: string
  providerDisplayName: string
  model: string
  endpoint: string
}

/**
 * List all configured LLM provider instances
 */
export async function listLlmInstances(): Promise<{ instances: ProviderInstance[] }> {
  return fetchJson<{ instances: ProviderInstance[] }>("/api/llm/instances")
}

/**
 * Get LLM provider details
 */
export async function getLlmProvider(providerName: string): Promise<{ provider: ProviderPublic }> {
  return fetchJson<{ provider: ProviderPublic }>(`/api/llm/provider/${encodeURIComponent(providerName)}`)
}

/**
 * Get API key status (masked, with optional reveal)
 */
export async function getApiKeyStatus(providerName: string, reveal = false): Promise<ApiKeyStatusResponse> {
  const q = reveal ? "?reveal=true" : ""
  return fetchJson<ApiKeyStatusResponse>(`/api/llm/api-key/${encodeURIComponent(providerName)}${q}`)
}

/**
 * Set LLM API key
 */
export async function setLlmApiKey(providerName: string, apiKey: string): Promise<{ ok: boolean }> {
  return fetchJson<{ ok: boolean }>("/api/llm/api-key", {
    method: "POST",
    body: JSON.stringify({ providerName, apiKey }),
  })
}

/**
 * Delete LLM API key
 */
export async function deleteLlmApiKey(providerName: string): Promise<{ ok: boolean }> {
  return fetchJson<{ ok: boolean }>(`/api/llm/api-key/${encodeURIComponent(providerName)}`, {
    method: "DELETE",
  })
}

/**
 * Test LLM provider connection
 */
export async function testLlmProvider(providerName: string): Promise<TestProviderResponse> {
  return fetchJson<TestProviderResponse>(`/api/llm/test/${encodeURIComponent(providerName)}`)
}

/**
 * Fetch available models for a provider
 */
export async function fetchLlmModels(providerName: string, limit = 200): Promise<FetchModelsResponse> {
  return fetchJson<FetchModelsResponse>(`/api/llm/models/${encodeURIComponent(providerName)}?limit=${limit}`)
}

/**
 * Set secret value
 */
export async function setSecret(key: string, value: string): Promise<{ ok: boolean }> {
  return fetchJson<{ ok: boolean }>("/api/secrets/set", {
    method: "POST",
    body: JSON.stringify({ key, value }),
  })
}

/**
 * Remove secret
 */
export async function removeSecret(key: string): Promise<{ ok: boolean; removed: boolean }> {
  return fetchJson<{ ok: boolean; removed: boolean }>("/api/secrets/remove", {
    method: "POST",
    body: JSON.stringify({ key }),
  })
}

/**
 * Save provider endpoint override to secrets
 */
export async function saveProviderEndpoint(providerName: string, endpoint: string): Promise<{ ok: boolean }> {
  const key = `LLMProviders:Providers:${providerName}:Endpoint`
  if (!endpoint.trim()) {
    return removeSecret(key)
  }
  return setSecret(key, endpoint.trim())
}

/**
 * Save provider model override to secrets
 */
export async function saveProviderModel(providerName: string, model: string): Promise<{ ok: boolean }> {
  const key = `LLMProviders:Providers:${providerName}:Model`
  if (!model.trim()) {
    return removeSecret(key)
  }
  return setSecret(key, model.trim())
}

// ============================================================================
//  Tools & MCP
// ============================================================================

/**
 * Tool summary type
 */
export interface ToolSummary {
  name: string
  description?: string
  category?: string
  source?: string // 'MCP' | 'AGENT_SKILLS' | etc.
  tags?: string[]
}

/**
 * Get tools snapshot for a session
 */
export async function getToolsSnapshot(sessionId: string | null | undefined): Promise<{ tools: ToolSummary[] }> {
  if (!sessionId) {
    console.warn('[axiom-client] getToolsSnapshot called with invalid sessionId:', sessionId);
    return { tools: [] };
  }
  return fetchJson<{ tools: ToolSummary[] }>(`/api/sessions/${encodeURIComponent(sessionId)}/tools`)
}

/**
 * Reconnect MCP server
 */
export async function reconnectMcp(sessionId: string | null | undefined): Promise<unknown> {
  if (!sessionId) {
    console.warn('[axiom-client] reconnectMcp called with invalid sessionId:', sessionId);
    return { ok: false, error: 'Invalid sessionId' };
  }
  return fetchJson<unknown>(`/api/sessions/${encodeURIComponent(sessionId)}/mcp/reconnect`, {
    method: "POST",
  })
}

/**
 * Sync skills packs
 */
export async function syncSkills(): Promise<unknown> {
  return fetchJson<unknown>("/api/skills/sync", {
    method: "POST",
  })
}

/**
 * Get skills sync status
 */
export async function getSkillsSyncStatus(): Promise<unknown> {
  return fetchJson<unknown>("/api/skills/sync/status")
}

// ============================================================================
//  SkillsMP Marketplace API
// ============================================================================

/**
 * SkillsMP status response
 */
export interface SkillsMpStatus {
  ok: boolean
  configured: boolean
  masked: string
  keyPath?: string
}

/**
 * SkillsMP search item
 */
export interface SkillsMpItem {
  id?: string
  name?: string
  description?: string
  repoUrl?: string
  url?: string
  stars?: number
}

/**
 * SkillsMP search response
 */
export interface SkillsMpSearchResponse {
  ok: boolean
  items: SkillsMpItem[]
  total?: number
}

/**
 * Get SkillsMP API key status
 */
export async function getSkillsMpStatus(): Promise<SkillsMpStatus> {
  return fetchJson<SkillsMpStatus>("/api/skillsmp/status")
}

/**
 * Search skills in SkillsMP marketplace
 */
export async function searchSkillsMp(
  query: string,
  mode: "search" | "ai-search" = "search",
  options?: { page?: number; limit?: number; sortBy?: string }
): Promise<SkillsMpSearchResponse> {
  const params = new URLSearchParams({ q: query })
  if (options?.page) params.set("page", String(options.page))
  if (options?.limit) params.set("limit", String(options.limit))
  if (options?.sortBy) params.set("sortBy", options.sortBy)

  const endpoint = mode === "ai-search" ? "/api/skillsmp/ai-search" : "/api/skillsmp/search"
  return fetchJson<SkillsMpSearchResponse>(`${endpoint}?${params}`)
}

/**
 * Install skill pack configuration
 */
export interface SkillPackConfig {
  name?: string
  repoUrl: string
  ref?: string
  skillsSubDir?: string
  sync?: boolean
}

/**
 * Install a skill pack from git repository
 */
export async function installSkillPack(config: SkillPackConfig): Promise<unknown> {
  return fetchJson<unknown>("/api/skillsmp/install", {
    method: "POST",
    body: JSON.stringify({
      name: config.name || undefined,
      repoUrl: config.repoUrl,
      ref: config.ref || "main",
      skillsSubDir: config.skillsSubDir || "skills",
      sync: config.sync ?? true,
    }),
  })
}

// ============================================================================
//  Per-Agent Provider Configuration
// ============================================================================

/**
 * Get agent providers mapping
 */
export async function getAgentProviders(sessionId: string | null | undefined): Promise<unknown> {
  if (!sessionId) {
    console.warn('[axiom-client] getAgentProviders called with invalid sessionId:', sessionId);
    return {};
  }
  return fetchJson<unknown>(`/api/sessions/${encodeURIComponent(sessionId)}/agent-providers`)
}

/**
 * Set agent provider
 */
export async function setAgentProvider(sessionId: string | null | undefined, agent: string, providerName: string): Promise<unknown> {
  if (!sessionId) {
    console.warn('[axiom-client] setAgentProvider called with invalid sessionId:', sessionId);
    return { ok: false, error: 'Invalid sessionId' };
  }
  return fetchJson<unknown>(`/api/sessions/${encodeURIComponent(sessionId)}/agent-providers`, {
    method: "PUT",
    body: JSON.stringify({ agent, providerName }),
  })
}

// ============================================================================
//  Knowledge Graph Enhanced APIs (FR-007/008, US4-US7)
// ============================================================================

/**
 * Node Explanation result
 */
export interface NodeExplanation {
  nodeId: string
  title: string
  kind: "Plan" | "Knowledge"
  markdownContent: string
  directDependencies: string[]
  fullChainNodeIds: string[]
  dependents: string[]
}

/**
 * Get detailed node explanation (US4)
 */
export async function getNodeExplanation(
  sessionId: string | null | undefined,
  nodeId: string
): Promise<NodeExplanation | null> {
  if (!sessionId || !nodeId) {
    console.warn('[axiom-client] getNodeExplanation called with invalid params:', { sessionId, nodeId });
    return null;
  }
  try {
    const result = await fetchJson<{ explanation?: NodeExplanation }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/${encodeURIComponent(nodeId)}/explain`
    )
    return result?.explanation ?? null
  } catch {
    return null
  }
}

/**
 * Session Summary result
 */
export interface SessionSummary {
  sessionId: string
  status: string
  markdownContent: string
  planNodeCount: number
  knowledgeNodeCount: number
  progressPercentage: number
}

/**
 * DAG Summary result
 */
export interface DagSummary {
  sessionId: string
  markdownContent: string
  totalNodes: number
  totalEdges: number
  maxDepth: number
}

/**
 * Get session summary (US7)
 */
export async function getSessionSummary(
  sessionId: string | null | undefined
): Promise<SessionSummary | null> {
  if (!sessionId) {
    console.warn('[axiom-client] getSessionSummary called with invalid sessionId:', sessionId);
    return null;
  }
  try {
    const result = await fetchJson<{ summary?: SessionSummary }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/summary`
    )
    return result?.summary ?? null
  } catch {
    return null
  }
}

/**
 * Get full DAG summary (US7)
 */
export async function getFullDagSummary(
  sessionId: string | null | undefined
): Promise<DagSummary | null> {
  if (!sessionId) {
    console.warn('[axiom-client] getFullDagSummary called with invalid sessionId:', sessionId);
    return null;
  }
  try {
    const result = await fetchJson<{ summary?: DagSummary }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/dag-summary`
    )
    return result?.summary ?? null
  } catch {
    return null
  }
}

/**
 * Pivot Snapshot result
 */
export interface PivotSnapshot {
  id: string
  createdAt: string
  reason: string
  nodeCount: number
  edgeCount: number
}

/**
 * Create pivot snapshot (US6)
 */
export async function createPivotSnapshot(
  sessionId: string | null | undefined,
  reason: string
): Promise<{ snapshotId?: string; error?: string }> {
  if (!sessionId) {
    console.warn('[axiom-client] createPivotSnapshot called with invalid sessionId:', sessionId);
    return { error: 'Invalid sessionId' };
  }
  try {
    return await fetchJson<{ snapshotId?: string; error?: string }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/pivot`,
      {
        method: "POST",
        body: JSON.stringify({ reason }),
      }
    )
  } catch (e) {
    return { error: (e as Error)?.message || 'Unknown error' }
  }
}

/**
 * Get pivot snapshots (US6)
 */
export async function getPivotSnapshots(
  sessionId: string | null | undefined
): Promise<PivotSnapshot[]> {
  if (!sessionId) {
    console.warn('[axiom-client] getPivotSnapshots called with invalid sessionId:', sessionId);
    return [];
  }
  try {
    const result = await fetchJson<{ snapshots?: PivotSnapshot[] }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/pivots`
    )
    return result?.snapshots ?? []
  } catch {
    return []
  }
}

// ============================================================================
//  File Upload with Knowledge Extraction
// ============================================================================

/**
 * Extracted knowledge node returned from upload extraction
 */
export interface ExtractedKnowledgeNode {
  id: string
  title: string
  content: string
  keywords: string[]
}

/**
 * Upload extraction response
 */
export interface UploadExtractionResponse {
  ok: boolean
  sessionId?: string
  fileName?: string
  filePath?: string
  message?: string
  error?: string
  extractedNodes?: ExtractedKnowledgeNode[]
}

/**
 * Upload a file and extract ALL knowledge points to create KnowledgeNodes in the graph.
 * Uses LLM to thoroughly analyze the file content and extract as many distinct
 * knowledge points as possible, which are then stored in the global DAG.
 *
 * @param sessionId - The session ID
 * @param file - The file to upload (supports .txt, .md, .json, .csv, .pdf)
 * @param options - Optional configuration
 * @param options.providerName - Optional LLM provider to use
 * @param options.maxKnowledgePoints - Optional limit (default: 0 = unlimited, extract all)
 * @returns Extraction result with created knowledge nodes
 */
export async function uploadWithExtraction(
  sessionId: string | null | undefined,
  file: File,
  options?: {
    providerName?: string
    /** Max points to extract. 0 or undefined = unlimited (extract all) */
    maxKnowledgePoints?: number
  }
): Promise<UploadExtractionResponse> {
  if (!sessionId) {
    console.warn('[axiom-client] uploadWithExtraction called with invalid sessionId:', sessionId)
    return { ok: false, error: 'Invalid sessionId' }
  }

  if (!file) {
    console.warn('[axiom-client] uploadWithExtraction called with no file')
    return { ok: false, error: 'No file provided' }
  }

  const formData = new FormData()
  formData.append('file', file)

  // Build query params for options
  const params = new URLSearchParams()
  if (options?.providerName) {
    params.set('providerName', options.providerName)
  }
  if (options?.maxKnowledgePoints) {
    params.set('maxKnowledgePoints', String(options.maxKnowledgePoints))
  }

  const queryString = params.toString()
  const url = `${API_BASE}/api/sessions/${encodeURIComponent(sessionId)}/uploads/extract${queryString ? `?${queryString}` : ''}`

  try {
    const sessionController = getSessionAbortController()
    const res = await fetch(url, {
      method: 'POST',
      body: formData,
      signal: sessionController.signal,
      // Note: Don't set Content-Type header - browser will set it with boundary for FormData
    })

    if (!res.ok) {
      const text = await res.text()
      return { ok: false, error: `Upload failed: ${res.status} ${text}` }
    }

    const result = await res.json() as UploadExtractionResponse
    return result
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unknown error'
    console.error('[axiom-client] uploadWithExtraction error:', message)
    return { ok: false, error: message }
  }
}

// ============================================================================
//  Review Agent API
// ============================================================================

import type {
  ReviewAgentSettings,
  ReviewAgentSettingsUpdate,
  ReviewAgentStatus,
  IterationListResponse,
  ReviewIteration,
  ReviewGraphResponse,
} from '../types/review-agent'

/**
 * Get Review Agent status
 * Normalizes PascalCase response from .NET backend to camelCase
 */
export async function getReviewAgentStatus(): Promise<ReviewAgentStatus & { isRunning?: boolean; hasStarted?: boolean }> {
  const data = await fetchJson<Record<string, unknown>>('/api/review-agent/status', undefined, { cache: false })
  return {
    status: (data.status ?? data.Status ?? 'Idle') as ReviewAgentStatus['status'],
    currentIterationId: (data.currentIterationId ?? data.CurrentIterationId ?? null) as string | null,
    lastCompletedAt: (data.lastCompletedAt ?? data.LastCompletedAt ?? null) as string | null,
    nextScheduledAt: (data.nextScheduledAt ?? data.NextScheduledAt ?? null) as string | null,
    nodesReviewed: (data.nodesReviewed ?? data.NodesReviewed ?? 0) as number,
    nodesPending: (data.nodesPending ?? data.NodesPending ?? 0) as number,
    nodesDeactivated: (data.nodesDeactivated ?? data.NodesDeactivated ?? 0) as number,
    nodesRemoved: (data.nodesRemoved ?? data.NodesRemoved ?? 0) as number,
    errorMessage: (data.errorMessage ?? data.ErrorMessage ?? null) as string | null,
    // Extended fields for manual trigger state
    isRunning: (data.isRunning ?? data.IsRunning) as boolean | undefined,
    hasStarted: (data.hasStarted ?? data.HasStarted) as boolean | undefined,
  }
}

/**
 * Get Review Agent settings
 * Normalizes PascalCase response from .NET backend to camelCase
 */
export async function getReviewAgentSettings(): Promise<ReviewAgentSettings> {
  const data = await fetchJson<Record<string, unknown>>('/api/review-agent/settings')
  return {
    iterationIntervalMinutes: (data.iterationIntervalMinutes ?? data.IterationIntervalMinutes ?? 30) as number,
    outOfDateThresholdMinutes: (data.outOfDateThresholdMinutes ?? data.OutOfDateThresholdMinutes ?? 60) as number,
    toDeleteThresholdMinutes: (data.toDeleteThresholdMinutes ?? data.ToDeleteThresholdMinutes ?? 1440) as number,
    llmProviderName: (data.llmProviderName ?? data.LLMProviderName ?? 'default') as string,
    perNodeTimeoutSeconds: (data.perNodeTimeoutSeconds ?? data.PerNodeTimeoutSeconds ?? 120) as number,
  }
}

/**
 * Update Review Agent settings
 */
export async function updateReviewAgentSettings(
  settings: ReviewAgentSettingsUpdate
): Promise<ReviewAgentSettings> {
  return fetchJson<ReviewAgentSettings>('/api/review-agent/settings', {
    method: 'PUT',
    body: JSON.stringify(settings),
  })
}

/**
 * Get Review Agent iterations (history)
 */
export async function getReviewAgentIterations(
  limit = 10,
  offset = 0
): Promise<IterationListResponse> {
  return fetchJson<IterationListResponse>(
    `/api/review-agent/iterations?limit=${limit}&offset=${offset}`
  )
}

/**
 * Get Review Agent iteration detail
 */
export async function getReviewAgentIteration(
  iterationId: string
): Promise<ReviewIteration> {
  return fetchJson<ReviewIteration>(
    `/api/review-agent/iterations/${encodeURIComponent(iterationId)}`
  )
}

/**
 * Get Review Agent graph data
 */
export async function getReviewAgentGraph(): Promise<ReviewGraphResponse> {
  return fetchJson<ReviewGraphResponse>('/api/review-agent/graph')
}

/**
 * Get current Review Agent iteration entries
 */
export async function getReviewAgentCurrentEntries(): Promise<{
  iterationId?: string
  entries: Array<{
    entryId?: string
    nodeId: string
    nodeLabel: string
    explainContent?: string | null
    result: string
    deactivatedReason?: string | null
    timestamp: string
    verificationContent?: string | null
  }>
}> {
  return fetchJson('/api/review-agent/current-entries')
}

/**
 * Trigger a Review Agent iteration
 */
export async function triggerReviewAgent(): Promise<{
  triggered: boolean
  message: string
  isRunning?: boolean
  hasStarted?: boolean
}> {
  return fetchJson('/api/review-agent/trigger', { method: 'POST' })
}

// === Export API Base for Vite proxy configuration ===
export { API_BASE }
