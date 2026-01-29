// ============================================================
//  Agent Flow Graph - React Flow Container
//  Visualizes Maker consensus, Agent communication, Tool calls
//  Fully dependent on backend data - no mock/default nodes
// ============================================================

import React, { useMemo, useCallback, useState } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  useNodesState,
  useEdgesState,
  ConnectionMode,
  MarkerType,
} from '@xyflow/react'
import type { Node, Edge } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { motion } from 'framer-motion'
import { Activity, Loader2 } from 'lucide-react'
import CoordinatorNode from './nodes/coordinator-node'
import WorkerNode from './nodes/worker-node'
import ToolNode from './nodes/tool-node'
import AnimatedEdge from './edges/animated-edge'
import VotingStatusPanel from './voting-status-panel'
import ViewOptionsBar from './view-options-bar'
import SvgFilters from './effects/svg-filters'
import type { ClassifiedEvent, VotingStatus, FlowGraphState } from '@/types'

interface AgentFlowGraphProps {
  sessionId: string
  events: ClassifiedEvent[]
  votingStatus: VotingStatus | null
}

// Custom node types
const nodeTypes = {
  coordinator: CoordinatorNode,
  worker: WorkerNode,
  tool: ToolNode,
}

// Custom edge types
const edgeTypes = {
  animated: AnimatedEdge,
}

// Worker colors
const WORKER_COLORS = [
  '#00f0ff', // cyan
  '#22c55e', // green
  '#ffd700', // gold
  '#a855f7', // purple
  '#f43f5e', // rose
  '#3b82f6', // blue
]

const AgentFlowGraph: React.FC<AgentFlowGraphProps> = ({
  sessionId: _sessionId,
  events,
  votingStatus,
}) => {
  void _sessionId // Reserved for event filtering by session
  
  // Flow graph state
  const [flowState, setFlowState] = useState<FlowGraphState>({
    visibleLayers: {
      maker: true,
      vibe: true,
      tool: true,
    },
    detailMode: true,
    focusMode: false,
    focusedNodeId: null,
  })

  // Check if we have any data
  const hasData = events.length > 0 || votingStatus !== null

  // Build nodes and edges from events (only when we have data)
  const { initialNodes, initialEdges } = useMemo(() => {
    const nodes: Node[] = []
    const edges: Edge[] = []

    // No data - return empty graph
    if (!hasData) {
      return { initialNodes: nodes, initialEdges: edges }
    }

    // Count vote/consensus events
    const voteEventCount = events.filter(e => e.category === 'consensus' || e.category === 'vote').length

    // Coordinator node (only if we have events)
    if (events.length > 0) {
      nodes.push({
        id: 'coordinator',
        type: 'coordinator',
        position: { x: 400, y: 50 },
        data: {
          label: 'Coordinator',
          status: voteEventCount > 0 ? 'running' : 'idle',
          messageCount: voteEventCount,
        },
      })
    }

    // Worker nodes (only from actual voting status data)
    const workers = votingStatus?.workers || []
    
    if (workers.length > 0) {
      workers.forEach((worker, i) => {
        const angle = (Math.PI / (workers.length + 1)) * (i + 1)
        const radius = 200
        const x = 400 + Math.cos(angle - Math.PI / 2) * radius
        const y = 150 + Math.sin(angle - Math.PI / 2) * radius + 100
        
        nodes.push({
          id: `worker-${i}`,
          type: 'worker',
          position: { x, y },
          data: {
            label: worker.name,
            votes: worker.votes,
            isLeader: worker.isLeader,
            color: worker.color || WORKER_COLORS[i % WORKER_COLORS.length],
            status: worker.isLeader ? 'completed' : 'idle',
          },
        })

        // Edge from Coordinator to Worker
        if (nodes.some(n => n.id === 'coordinator')) {
          edges.push({
            id: `coord-worker-${i}`,
            source: 'coordinator',
            target: `worker-${i}`,
            type: 'animated',
            animated: true,
            style: { stroke: worker.color || WORKER_COLORS[i % WORKER_COLORS.length], strokeWidth: 2 },
            data: { color: worker.color || WORKER_COLORS[i % WORKER_COLORS.length] },
          })
        }
      })

      // Vote edges between workers
      const leaderId = workers.findIndex(w => w.isLeader)
      if (leaderId >= 0) {
        workers.forEach((worker, i) => {
          if (i !== leaderId && worker.votes === 0) {
            edges.push({
              id: `vote-${i}-${leaderId}`,
              source: `worker-${i}`,
              target: `worker-${leaderId}`,
              type: 'animated',
              style: { stroke: '#22c55e', strokeWidth: 2, strokeDasharray: '5 5' },
              markerEnd: { type: MarkerType.ArrowClosed, color: '#22c55e' },
              data: { color: '#22c55e', isVote: true },
            })
          }
        })
      }
    }

    // Tool nodes (from tool_call events)
    const toolEvents = events.filter(e => e.category === 'tool_call')
    const toolNames = [...new Set(toolEvents.map(e => e.toolInfo?.toolName).filter(Boolean))]
    
    toolNames.forEach((toolName, i) => {
      if (!toolName) return
      const callCount = toolEvents.filter(e => e.toolInfo?.toolName === toolName).length
      const x = 100 + i * 150
      const y = 400
      
      nodes.push({
        id: `tool-${toolName}`,
        type: 'tool',
        position: { x, y },
        data: {
          label: toolName,
          callCount,
          status: 'idle',
        },
      })

      // Edge from first worker (if exists) to tool
      const firstWorkerNode = nodes.find(n => n.id.startsWith('worker-'))
      if (firstWorkerNode) {
        edges.push({
          id: `worker-tool-${toolName}`,
          source: firstWorkerNode.id,
          target: `tool-${toolName}`,
          type: 'animated',
          style: { stroke: '#f43f5e', strokeWidth: 1, strokeDasharray: '4 4' },
          data: { color: '#f43f5e' },
        })
      }
    })

    return { initialNodes: nodes, initialEdges: edges }
  }, [events, votingStatus, hasData])

  const [nodes, , onNodesChange] = useNodesState(initialNodes)
  const [edges, , onEdgesChange] = useEdgesState(initialEdges)

  // Filter nodes based on layer visibility
  const visibleNodes = useMemo(() => {
    return nodes.filter(node => {
      if (node.type === 'coordinator' || node.type === 'worker') {
        return flowState.visibleLayers.maker
      }
      if (node.type === 'tool') {
        return flowState.visibleLayers.tool
      }
      return true
    })
  }, [nodes, flowState.visibleLayers])

  // Filter edges based on layer visibility
  const visibleEdges = useMemo(() => {
    const visibleNodeIds = new Set(visibleNodes.map(n => n.id))
    return edges.filter(edge => {
      return visibleNodeIds.has(edge.source) && visibleNodeIds.has(edge.target)
    })
  }, [edges, visibleNodes])

  // Toggle layer visibility
  const toggleLayer = useCallback((layer: 'maker' | 'vibe' | 'tool') => {
    setFlowState(prev => ({
      ...prev,
      visibleLayers: {
        ...prev.visibleLayers,
        [layer]: !prev.visibleLayers[layer],
      },
    }))
  }, [])

  // Toggle detail mode
  const toggleDetailMode = useCallback(() => {
    setFlowState(prev => ({
      ...prev,
      detailMode: !prev.detailMode,
    }))
  }, [])

  // Reset view
  const resetView = useCallback(() => {
    setFlowState({
      visibleLayers: { maker: true, vibe: true, tool: true },
      detailMode: true,
      focusMode: false,
      focusedNodeId: null,
    })
  }, [])

  // Empty state - waiting for events
  if (!hasData) {
    return (
      <div className="h-full w-full relative bg-[#0a0a12] flex items-center justify-center">
        <motion.div
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          className="flex flex-col items-center gap-4 text-center"
        >
          {/* Animated icon */}
          <div className="relative">
            <motion.div
              className="absolute inset-0 rounded-full bg-neon-cyan/20 blur-xl"
              animate={{ opacity: [0.3, 0.6, 0.3], scale: [1, 1.1, 1] }}
              transition={{ repeat: Infinity, duration: 2 }}
            />
            <div className="relative flex h-16 w-16 items-center justify-center rounded-full bg-bg-surface border border-neon-cyan/30">
              <Activity className="h-8 w-8 text-neon-cyan" />
            </div>
          </div>

          {/* Loading indicator */}
          <div className="flex items-center gap-2 text-text-muted">
            <Loader2 className="h-4 w-4 animate-spin text-neon-cyan" />
            <span className="text-sm font-mono">Waiting for events...</span>
          </div>

          {/* Hint */}
          <p className="text-xs text-text-dimmed max-w-xs">
            The flow graph will populate automatically when workflow events are received from the backend.
          </p>
        </motion.div>

        {/* Background grid */}
        <div 
          className="absolute inset-0 opacity-20"
          style={{
            backgroundImage: 'radial-gradient(circle at 1px 1px, #1a1a2e 1px, transparent 0)',
            backgroundSize: '20px 20px',
          }}
        />
      </div>
    )
  }

  return (
    <div className="h-full w-full relative bg-[#0a0a12]">
      {/* SVG Filters for glow effects */}
      <SvgFilters />

      {/* React Flow */}
      <ReactFlow
        nodes={visibleNodes}
        edges={visibleEdges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        connectionMode={ConnectionMode.Loose}
        fitView
        fitViewOptions={{ padding: 0.2 }}
        className="bg-transparent"
        proOptions={{ hideAttribution: true }}
      >
        <Background
          color="#1a1a2e"
          gap={20}
          size={1}
          className="opacity-50"
        />
        <Controls
          className="!bg-bg-surface !border-border-subtle !shadow-lg"
          showZoom
          showFitView
          showInteractive={false}
        />
        <MiniMap
          className="!bg-bg-surface !border-border-subtle"
          nodeColor={(node): string => {
            if (node.type === 'coordinator') return '#00f0ff'
            if (node.type === 'worker') return (node.data?.color as string) || '#22c55e'
            if (node.type === 'tool') return '#f43f5e'
            return '#666'
          }}
          maskColor="rgba(0, 0, 0, 0.8)"
        />
      </ReactFlow>

      {/* View Options Bar */}
      <ViewOptionsBar
        visibleLayers={flowState.visibleLayers}
        detailMode={flowState.detailMode}
        onToggleLayer={toggleLayer}
        onToggleDetail={toggleDetailMode}
        onReset={resetView}
      />

      {/* Voting Status Panel */}
      {votingStatus && (
        <VotingStatusPanel
          status={votingStatus}
          className="absolute top-4 left-4"
        />
      )}
    </div>
  )
}

export default AgentFlowGraph
