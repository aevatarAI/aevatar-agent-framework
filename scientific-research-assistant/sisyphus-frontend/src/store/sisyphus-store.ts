import { create } from "zustand"
import type { 
  SisyphusSession, 
  WorkerAgent, 
  WorkerHistoryItem,
  ChatMessage, 
  ResearchBrief,
  SystemStats,
  DAGGraph,
  ToolSummary
} from "@/types"

// ============================================================================
//  Sisyphus Store - Zustand State Management
//  Integrated with @aevatar/kit for AxiomReasoning backend
// ============================================================================

interface ApiInfo {
  version?: string
  llm?: {
    default?: string
    providers?: string[]
    providersAll?: string[]
  }
}

// Input mode for Composer
export type InputMode = "chat" | "vibe" | "vibe_loop"

// Agent roster item
export interface AgentRosterItem {
  agent: string
  agentId?: string
}

// Per-agent message during a vibe run
export interface AgentMessage {
  agent: string
  content: string
  isStreaming: boolean
  isFinal: boolean
  tokenCount: number
  providerName?: string
  stepName?: string
  toolOutputs?: unknown[]
}

// DAG Node Explain data
export interface DagNodeExplainData {
  provable?: boolean
  hasCycle?: boolean
  directDeps?: string[]
  missing?: Array<{ id: string; type?: string }>
}

// Agent message metadata (from aevatar.vibe.message_meta)
export interface AgentMessageMeta {
  messageId: string
  agent: string
  stepName?: string
  providerName?: string
}

interface SisyphusState {
  // Connection
  isConnected: boolean
  setConnected: (connected: boolean) => void

  // API Info (LLM providers, version, etc.)
  apiInfo: ApiInfo | null
  setApiInfo: (info: ApiInfo | null) => void

  // Agent Providers (per-agent LLM config)
  agentProviders: Record<string, string>
  setAgentProviders: (providers: Record<string, string>) => void

  // Agent Roster (available agents from vibe)
  agentRoster: AgentRosterItem[]
  setAgentRoster: (roster: AgentRosterItem[]) => void

  // Input Mode
  inputMode: InputMode
  setInputMode: (mode: InputMode) => void

  // Sending state
  isSending: boolean
  setIsSending: (sending: boolean) => void

  // Current Run tracking
  currentRunId: string | null
  userPrompt: string
  agentMessages: Record<string, AgentMessage>  // keyed by agent name
  agentMessageMeta: Record<string, AgentMessageMeta>  // keyed by messageId
  setCurrentRun: (runId: string | null, prompt?: string) => void
  updateAgentMessage: (agent: string, update: Partial<AgentMessage>) => void
  setAgentMessageMeta: (meta: AgentMessageMeta) => void
  clearAgentMessages: () => void

  // Tools (available tools from MCP + Agent Skills)
  tools: ToolSummary[]
  setTools: (tools: ToolSummary[]) => void

  // Sessions
  sessions: SisyphusSession[]
  currentSessionId: string | null
  setSessions: (sessions: SisyphusSession[]) => void
  setCurrentSession: (id: string | null) => void
  updateSession: (session: Partial<SisyphusSession> & { id: string }) => void

  // Workers
  workers: Record<string, WorkerAgent>
  updateWorker: (worker: Partial<WorkerAgent> & { id: string }) => void
  appendWorkerHistory: (workerId: string, historyItem: WorkerHistoryItem) => void
  clearWorkers: () => void

  // DAG
  dag: DAGGraph | null
  selectedNodeId: string | null
  dagNodeExplain: DagNodeExplainData | null
  dagKnowledgeChain: string
  dagLoading: boolean
  dagError: string
  setDag: (dag: DAGGraph | null) => void
  setSelectedNode: (nodeId: string | null) => void
  setDagNodeExplain: (explain: DagNodeExplainData | null) => void
  setDagKnowledgeChain: (chain: string) => void
  setDagLoading: (loading: boolean) => void
  setDagError: (error: string) => void

  // Chat Messages
  messages: ChatMessage[]
  addMessage: (message: Omit<ChatMessage, "id" | "timestamp">) => void
  clearMessages: () => void

  // Research Brief
  researchBrief: ResearchBrief | null
  setResearchBrief: (brief: ResearchBrief | null) => void

  // System Stats
  stats: SystemStats
  updateStats: (stats: Partial<SystemStats>) => void

  // Raw Events (for debugging)
  rawEvents: unknown[]
  addRawEvent: (event: unknown) => void

  // Reset state for new session
  resetForNewSession: () => void
}

export const useSisyphusStore = create<SisyphusState>((set) => ({
  // === Connection ===
  isConnected: false,
  setConnected: (connected) => set({ isConnected: connected }),

  // === API Info ===
  apiInfo: null,
  setApiInfo: (info) => set({ apiInfo: info }),

  // === Agent Providers ===
  agentProviders: {},
  setAgentProviders: (providers) => set({ agentProviders: providers }),

  // === Agent Roster ===
  agentRoster: [],
  setAgentRoster: (roster) => set({ agentRoster: roster }),

  // === Input Mode ===
  inputMode: "vibe",
  setInputMode: (mode) => set({ inputMode: mode }),

  // === Sending State ===
  isSending: false,
  setIsSending: (sending) => set({ isSending: sending }),

  // === Current Run ===
  currentRunId: null,
  userPrompt: "",
  agentMessages: {},
  agentMessageMeta: {},
  setCurrentRun: (runId, prompt) => set({ 
    currentRunId: runId, 
    userPrompt: prompt ?? "",
    agentMessages: {}, // Clear agent messages for new run
    agentMessageMeta: {},
  }),
  updateAgentMessage: (agent, update) => set((state) => {
    const existing = state.agentMessages[agent] || {
      agent,
      content: "",
      isStreaming: false,
      isFinal: false,
      tokenCount: 0,
    }
    // Accumulate content if streaming
    const newContent = update.isStreaming && update.content
      ? existing.content + update.content
      : update.content ?? existing.content
    
    return {
      agentMessages: {
        ...state.agentMessages,
        [agent]: {
          ...existing,
          ...update,
          content: newContent,
          // Estimate tokens from content
          tokenCount: Math.max(1, Math.ceil(newContent.length / 4)),
        },
      },
    }
  }),
  setAgentMessageMeta: (meta) => set((state) => ({
    agentMessageMeta: {
      ...state.agentMessageMeta,
      [meta.messageId]: meta,
    },
  })),
  clearAgentMessages: () => set({ agentMessages: {}, agentMessageMeta: {}, currentRunId: null, userPrompt: "" }),

  // === Tools ===
  tools: [],
  setTools: (tools) => set({ tools }),

  // === Sessions ===
  sessions: [],
  currentSessionId: null,
  setSessions: (sessions) => set({ sessions }),
  setCurrentSession: (id) => set({ currentSessionId: id }),
  updateSession: (session) =>
    set((state) => ({
      sessions: state.sessions.map((s) =>
        s.id === session.id ? { ...s, ...session } : s
      ),
    })),

  // === Workers ===
  workers: {},
  updateWorker: (worker) =>
    set((state) => {
      const existing = state.workers[worker.id]
      // Build default worker first, then merge existing and updates
      const defaultWorker: WorkerAgent = {
        id: worker.id,
        name: worker.name || worker.id,
        status: "pending",
        streaming: false,
        streamContent: "",
        lastResponse: "",
        tokenIndex: 0,
        history: [],
      }
      return {
        workers: {
          ...state.workers,
          [worker.id]: {
            ...defaultWorker,
            ...existing,
            ...worker,
            // Preserve existing history if not explicitly provided
            history: worker.history ?? existing?.history ?? [],
            // Accumulate stream content
            streamContent: worker.streaming && worker.streamContent
              ? (existing?.streamContent || '') + worker.streamContent
              : (worker.streaming ? (existing?.streamContent || '') : ''),
          },
        },
      }
    }),
  // Append history item to worker
  appendWorkerHistory: (workerId, historyItem) =>
    set((state) => {
      const existing = state.workers[workerId]
      if (!existing) return state
      
      // Check if stepId already exists
      const existingIdx = existing.history.findIndex(h => h.stepId === historyItem.stepId)
      
      let newHistory
      if (existingIdx >= 0) {
        // Update existing entry
        newHistory = [...existing.history]
        const existingItem = newHistory[existingIdx]
        newHistory[existingIdx] = {
          ...existingItem,
          system: existingItem.system || historyItem.system,
          user: existingItem.user || historyItem.user,
          response: historyItem.response
            ? (existingItem.response || '') + historyItem.response
            : existingItem.response,
        }
      } else {
        // Add new entry (prepend, keep last 12)
        newHistory = [historyItem, ...existing.history.slice(0, 11)]
      }
      
      return {
        workers: {
          ...state.workers,
          [workerId]: {
            ...existing,
            history: newHistory,
          },
        },
      }
    }),
  clearWorkers: () => set({ workers: {} }),

  // === DAG ===
  dag: null,
  selectedNodeId: null,
  dagNodeExplain: null,
  dagKnowledgeChain: "",
  dagLoading: false,
  dagError: "",
  setDag: (dag) => set({ dag }),
  setSelectedNode: (nodeId) => set({ selectedNodeId: nodeId, dagNodeExplain: null, dagKnowledgeChain: "" }),
  setDagNodeExplain: (explain) => set({ dagNodeExplain: explain }),
  setDagKnowledgeChain: (chain) => set({ dagKnowledgeChain: chain }),
  setDagLoading: (loading) => set({ dagLoading: loading }),
  setDagError: (error) => set({ dagError: error }),

  // === Chat Messages ===
  messages: [],
  addMessage: (message) =>
    set((state) => ({
      messages: [
        ...state.messages,
        {
          ...message,
          id: crypto.randomUUID(),
          timestamp: Date.now(),
        },
      ],
    })),
  clearMessages: () => set({ messages: [] }),

  // === Research Brief ===
  researchBrief: null,
  setResearchBrief: (brief) => set({ researchBrief: brief }),

  // === System Stats ===
  stats: {
    computeLoad: 0,
    networkIO: 0,
    latency: 0,
    memoryUsage: 0,
  },
  updateStats: (stats) =>
    set((state) => ({
      stats: { ...state.stats, ...stats },
    })),

  // === Raw Events ===
  rawEvents: [],
  addRawEvent: (event) =>
    set((state) => ({
      rawEvents: [...state.rawEvents.slice(-99), event],
    })),

  // === Reset for new session ===
  resetForNewSession: () =>
    set({
      workers: {},
      dag: null,
      selectedNodeId: null,
      dagNodeExplain: null,
      dagKnowledgeChain: "",
      dagLoading: false,
      dagError: "",
      messages: [],
      researchBrief: null,
      rawEvents: [],
      agentProviders: {},
      agentRoster: [],
      agentMessages: {},
      agentMessageMeta: {},
      currentRunId: null,
      userPrompt: "",
      isSending: false,
      tools: [],
      stats: {
        computeLoad: 0,
        networkIO: 0,
        latency: 0,
        memoryUsage: 0,
      },
    }),
}))
