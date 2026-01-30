// ============================================================
//  Agent Topology Store
//  State management for Agent Flow Graph visualization
//  Tracks topology structure and real-time agent status
//  Supports both API fetch and SSE updates
// ============================================================

import { create } from 'zustand'
import { fetchJson, validateSessionId } from '@/lib/axiom-client/fetch'

// ------------------------------------------------------------
//  Types
// ------------------------------------------------------------

export interface TopologyNode {
  id: string
  type: string
}

export interface TopologyEdge {
  from: string
  to: string
}

export interface Topology {
  nodes: TopologyNode[]
  edges: TopologyEdge[]
}

export type AgentStatus = 'idle' | 'running' | 'completed' | 'error'

// Agent statistics for enhanced visualization
export interface AgentStats {
  tokens: number           // Estimated token count
  startTime: number | null // Timestamp when agent started
  endTime: number | null   // Timestamp when agent finished
  duration: number | null  // Duration in ms (computed)
  messageCount: number     // Number of messages processed
  lastActivity: string     // Brief description of last activity
}

// Edge statistics for message flow visualization
export interface EdgeStats {
  messageCount: number     // Messages passed through this edge
  lastMessageTime: number | null
}

export interface AgentTopologyState {
  // Topology structure (from API or SSE mesh_started event)
  topology: Topology | null
  
  // Real-time status by agent type (e.g., 'planner' -> 'running')
  agentStatus: Record<string, AgentStatus>
  
  // Agent statistics by agent type
  agentStats: Record<string, AgentStats>
  
  // Edge statistics by edge ID (format: "from-to")
  edgeStats: Record<string, EdgeStats>
  
  // Currently selected agent for detail view
  selectedAgent: string | null
  
  // Current session ID that topology belongs to
  sessionId: string | null
  
  // Current run ID for tracking
  runId: string | null
  
  // Loading state for API fetch
  isLoading: boolean
  
  // Error message if fetch failed
  error: string | null
  
  // Actions
  setTopology: (topology: Topology, sessionId?: string, runId?: string) => void
  updateAgentStatus: (agentType: string, status: AgentStatus) => void
  updateAgentStats: (agentType: string, stats: Partial<AgentStats>) => void
  incrementEdgeMessages: (fromAgent: string, toAgent: string) => void
  setSelectedAgent: (agentType: string | null) => void
  fetchTopology: (sessionId: string) => Promise<void>
  reset: () => void
}

// ------------------------------------------------------------
//  Initial State
// ------------------------------------------------------------

// Helper to create default agent stats
const createDefaultStats = (): AgentStats => ({
  tokens: 0,
  startTime: null,
  endTime: null,
  duration: null,
  messageCount: 0,
  lastActivity: '',
})

const initialState = {
  topology: null as Topology | null,
  agentStatus: {} as Record<string, AgentStatus>,
  agentStats: {} as Record<string, AgentStats>,
  edgeStats: {} as Record<string, EdgeStats>,
  selectedAgent: null as string | null,
  sessionId: null as string | null,
  runId: null as string | null,
  isLoading: false,
  error: null as string | null,
}

// ------------------------------------------------------------
//  Store
// ------------------------------------------------------------

export const useAgentTopologyStore = create<AgentTopologyState>((set, get) => ({
  ...initialState,
  
  setTopology: (topology, sessionId, runId) => set({
    topology,
    sessionId: sessionId ?? get().sessionId,
    runId: runId ?? get().runId,
    error: null,
    // Initialize all agents as idle with default stats
    agentStatus: topology.nodes.reduce((acc, node) => {
      acc[node.type.toLowerCase()] = 'idle'
      return acc
    }, {} as Record<string, AgentStatus>),
    agentStats: topology.nodes.reduce((acc, node) => {
      acc[node.type.toLowerCase()] = createDefaultStats()
      return acc
    }, {} as Record<string, AgentStats>),
    edgeStats: topology.edges.reduce((acc, edge) => {
      acc[`${edge.from}-${edge.to}`] = { messageCount: 0, lastMessageTime: null }
      return acc
    }, {} as Record<string, EdgeStats>),
  }),
  
  updateAgentStatus: (agentType, status) => {
    const now = Date.now()
    const key = agentType.toLowerCase()
    const currentStats = get().agentStats[key] || createDefaultStats()
    
    // Track timing based on status transition
    let statsUpdate: Partial<AgentStats> = {}
    if (status === 'running' && !currentStats.startTime) {
      statsUpdate = { startTime: now, endTime: null, duration: null }
    } else if ((status === 'completed' || status === 'error') && currentStats.startTime) {
      const duration = now - currentStats.startTime
      statsUpdate = { endTime: now, duration }
    }
    
    set((state) => ({
      agentStatus: {
        ...state.agentStatus,
        [key]: status
      },
      agentStats: {
        ...state.agentStats,
        [key]: { ...currentStats, ...statsUpdate }
      }
    }))
  },
  
  updateAgentStats: (agentType, stats) => {
    const key = agentType.toLowerCase()
    set((state) => ({
      agentStats: {
        ...state.agentStats,
        [key]: { ...(state.agentStats[key] || createDefaultStats()), ...stats }
      }
    }))
  },
  
  incrementEdgeMessages: (fromAgent, toAgent) => {
    const edgeKey = `${fromAgent.toLowerCase()}-${toAgent.toLowerCase()}`
    set((state) => ({
      edgeStats: {
        ...state.edgeStats,
        [edgeKey]: {
          messageCount: (state.edgeStats[edgeKey]?.messageCount || 0) + 1,
          lastMessageTime: Date.now(),
        }
      }
    }))
  },
  
  setSelectedAgent: (agentType) => set({ selectedAgent: agentType }),
  
  fetchTopology: async (sessionId: string) => {
    // Validate sessionId
    if (!validateSessionId(sessionId, 'fetchTopology')) {
      return
    }
    
    // Skip if already loading
    if (get().isLoading) return
    
    // Skip if already have topology for this exact session
    if (get().sessionId === sessionId && get().topology) return
    
    // Clear old data if session changed (prevents showing stale data)
    const currentSessionId = get().sessionId
    if (currentSessionId && currentSessionId !== sessionId) {
      set({ topology: null, agentStatus: {}, agentStats: {}, edgeStats: {}, runId: null, selectedAgent: null })
    }
    
    set({ isLoading: true, error: null, sessionId })
    
    try {
      const data = await fetchJson<{ nodes: TopologyNode[]; edges: TopologyEdge[] }>(
        `/api/sessions/${encodeURIComponent(sessionId)}/mesh-topology`,
        undefined,
        { cache: false }  // Don't cache topology requests
      )
      
      if (data?.nodes && data?.edges) {
        set({
          topology: { nodes: data.nodes, edges: data.edges },
          sessionId,
          isLoading: false,
          error: null,
          agentStatus: data.nodes.reduce((acc: Record<string, AgentStatus>, node: TopologyNode) => {
            acc[node.type.toLowerCase()] = 'idle'
            return acc
          }, {} as Record<string, AgentStatus>),
          agentStats: data.nodes.reduce((acc: Record<string, AgentStats>, node: TopologyNode) => {
            acc[node.type.toLowerCase()] = createDefaultStats()
            return acc
          }, {} as Record<string, AgentStats>),
          edgeStats: data.edges.reduce((acc: Record<string, EdgeStats>, edge: TopologyEdge) => {
            acc[`${edge.from}-${edge.to}`] = { messageCount: 0, lastMessageTime: null }
            return acc
          }, {} as Record<string, EdgeStats>),
        })
      } else {
        set({ isLoading: false })
      }
    } catch (err) {
      // 404 = no mesh definition yet, not an error
      const errorMsg = err instanceof Error ? err.message : 'Failed to fetch topology'
      if (errorMsg.includes('404')) {
        set({ isLoading: false, error: null, topology: null })
      } else {
        set({ isLoading: false, error: errorMsg })
      }
    }
  },
  
  reset: () => set(initialState),
}))

// ------------------------------------------------------------
//  Selectors (fine-grained subscriptions)
// ------------------------------------------------------------

export const selectTopology = (state: AgentTopologyState) => state.topology
export const selectAgentStatus = (state: AgentTopologyState) => state.agentStatus
export const selectAgentStats = (state: AgentTopologyState) => state.agentStats
export const selectEdgeStats = (state: AgentTopologyState) => state.edgeStats
export const selectSelectedAgent = (state: AgentTopologyState) => state.selectedAgent
export const selectRunId = (state: AgentTopologyState) => state.runId
export const selectIsLoading = (state: AgentTopologyState) => state.isLoading
export const selectError = (state: AgentTopologyState) => state.error

// Selector for specific agent status
export const selectAgentStatusByType = (agentType: string) => 
  (state: AgentTopologyState) => state.agentStatus[agentType.toLowerCase()] || 'idle'

// Selector for specific agent stats
export const selectAgentStatsByType = (agentType: string) => 
  (state: AgentTopologyState) => state.agentStats[agentType.toLowerCase()] || createDefaultStats()

// Selector for edge stats
export const selectEdgeStatsByKey = (fromAgent: string, toAgent: string) =>
  (state: AgentTopologyState) => state.edgeStats[`${fromAgent.toLowerCase()}-${toAgent.toLowerCase()}`]
