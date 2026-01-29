import { create } from "zustand"

// ============================================================================
//  Stream Content Store - Isolated State for Real-time Streaming
//  
//  Purpose: Isolate high-frequency streaming updates from the main store
//  to prevent cascading re-renders across the entire component tree.
//  
//  React 18 automatic batching handles updates efficiently without flushSync.
//  Components subscribe to specific workerIds, so updates only affect relevant parts.
// ============================================================================

// ─────────────────────────────────────────────────────────────
//  Types
// ─────────────────────────────────────────────────────────────

export interface WorkerStreamState {
  content: string
  isStreaming: boolean
  tokenCount: number
}

export interface AgentStreamState {
  content: string
  isStreaming: boolean
  isFinal: boolean
  tokenCount: number
  providerName?: string
  stepName?: string
}

interface StreamContentState {
  // Worker streaming content (keyed by workerId)
  workerStreams: Record<string, WorkerStreamState>
  
  // Agent streaming content (keyed by agent name)
  agentStreams: Record<string, AgentStreamState>
  
  // Actions
  appendWorkerContent: (workerId: string, delta: string) => void
  setWorkerStreaming: (workerId: string, streaming: boolean) => void
  finalizeWorkerContent: (workerId: string) => string
  clearWorkerContent: (workerId: string) => void
  
  appendAgentContent: (agent: string, delta: string) => void
  setAgentStreaming: (agent: string, streaming: boolean) => void
  setAgentMeta: (agent: string, meta: { providerName?: string; stepName?: string }) => void
  finalizeAgentContent: (agent: string) => void
  clearAgentContent: (agent: string) => void
  
  // Batch operations
  clearAllStreams: () => void
}

// ─────────────────────────────────────────────────────────────
//  Default States
// ─────────────────────────────────────────────────────────────

const createDefaultWorkerStream = (): WorkerStreamState => ({
  content: "",
  isStreaming: false,
  tokenCount: 0,
})

const createDefaultAgentStream = (): AgentStreamState => ({
  content: "",
  isStreaming: false,
  isFinal: false,
  tokenCount: 0,
})

// ─────────────────────────────────────────────────────────────
//  Store Implementation
// ─────────────────────────────────────────────────────────────

export const useStreamContentStore = create<StreamContentState>((set, get) => ({
  workerStreams: {},
  agentStreams: {},

  // ── Worker Stream Actions ──
  
  appendWorkerContent: (workerId, delta) => {
    set((state) => {
      const existing = state.workerStreams[workerId] || createDefaultWorkerStream()
      const newContent = existing.content + delta
      return {
        workerStreams: {
          ...state.workerStreams,
          [workerId]: {
            ...existing,
            content: newContent,
            isStreaming: true,
            tokenCount: Math.ceil(newContent.length / 4),
          },
        },
      }
    })
  },

  setWorkerStreaming: (workerId, streaming) => {
    set((state) => {
      const existing = state.workerStreams[workerId]
      if (!existing) return state
      return {
        workerStreams: {
          ...state.workerStreams,
          [workerId]: { ...existing, isStreaming: streaming },
        },
      }
    })
  },

  finalizeWorkerContent: (workerId) => {
    const state = get()
    const existing = state.workerStreams[workerId]
    const content = existing?.content || ""
    
    set((s) => ({
      workerStreams: {
        ...s.workerStreams,
        [workerId]: {
          ...(s.workerStreams[workerId] || createDefaultWorkerStream()),
          isStreaming: false,
        },
      },
    }))
    
    return content
  },

  clearWorkerContent: (workerId) => {
    set((state) => {
      const { [workerId]: _, ...rest } = state.workerStreams
      return { workerStreams: rest }
    })
  },

  // ── Agent Stream Actions ──
  
  appendAgentContent: (agent, delta) => {
    set((state) => {
      const existing = state.agentStreams[agent] || createDefaultAgentStream()
      const newContent = existing.content + delta
      return {
        agentStreams: {
          ...state.agentStreams,
          [agent]: {
            ...existing,
            content: newContent,
            isStreaming: true,
            isFinal: false,
            tokenCount: Math.ceil(newContent.length / 4),
          },
        },
      }
    })
  },

  setAgentStreaming: (agent, streaming) => {
    set((state) => {
      const existing = state.agentStreams[agent]
      if (!existing) return state
      return {
        agentStreams: {
          ...state.agentStreams,
          [agent]: { ...existing, isStreaming: streaming },
        },
      }
    })
  },

  setAgentMeta: (agent, meta) => {
    set((state) => {
      const existing = state.agentStreams[agent] || createDefaultAgentStream()
      return {
        agentStreams: {
          ...state.agentStreams,
          [agent]: {
            ...existing,
            providerName: meta.providerName ?? existing.providerName,
            stepName: meta.stepName ?? existing.stepName,
          },
        },
      }
    })
  },

  finalizeAgentContent: (agent) => {
    set((state) => {
      const existing = state.agentStreams[agent]
      if (!existing) return state
      return {
        agentStreams: {
          ...state.agentStreams,
          [agent]: {
            ...existing,
            isStreaming: false,
            isFinal: true,
          },
        },
      }
    })
  },

  clearAgentContent: (agent) => {
    set((state) => {
      const { [agent]: _, ...rest } = state.agentStreams
      return { agentStreams: rest }
    })
  },

  // ── Batch Operations ──
  
  clearAllStreams: () => {
    set({ workerStreams: {}, agentStreams: {} })
  },
}))

// ─────────────────────────────────────────────────────────────
//  Selectors (for fine-grained subscriptions)
// ─────────────────────────────────────────────────────────────

/**
 * Get worker stream content by ID
 * Components using this selector only re-render when THIS worker changes
 */
export const selectWorkerStream = (workerId: string) => 
  (state: StreamContentState) => state.workerStreams[workerId]

/**
 * Get agent stream content by name
 * Components using this selector only re-render when THIS agent changes
 */
export const selectAgentStream = (agent: string) => 
  (state: StreamContentState) => state.agentStreams[agent]

/**
 * Get all agent names that have stream content
 */
export const selectAgentNames = (state: StreamContentState) => 
  Object.keys(state.agentStreams)

/**
 * Get all worker IDs that have stream content
 */
export const selectWorkerIds = (state: StreamContentState) => 
  Object.keys(state.workerStreams)

/**
 * Get streaming agent count (for status display)
 */
export const selectStreamingAgentCount = (state: StreamContentState) => 
  Object.values(state.agentStreams).filter(s => s.isStreaming).length
