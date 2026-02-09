// ============================================================
//  Agent Flow Graph - React Flow Container
//  Visualizes Maker consensus, Agent communication, Tool calls
//  Supports both real backend data and inferred workers
// ============================================================

import React, { useMemo, useCallback, useState, useEffect, useRef } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  useNodesState,
  useEdgesState,
  ConnectionMode,
} from '@xyflow/react'
import type { Node, Edge } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { motion } from 'framer-motion'
import { Activity, Loader2 } from 'lucide-react'
import CoordinatorNode from './nodes/coordinator-node'
import WorkerNode from './nodes/worker-node'
import ToolNode from './nodes/tool-node'
import ConsensusNode from './nodes/consensus-node'
import AnimatedEdge from './edges/animated-edge'
import VotingStatusPanel from './voting-status-panel'
import ViewOptionsBar from './view-options-bar'
import SvgFilters from './effects/svg-filters'
import WorkerDetailModal, { type WorkerDetail } from './worker-detail-modal'
import CoordinatorDetailModal from './coordinator-detail-modal'
import { buildInferredVotingStatus, needsWorkerInference } from '@/lib/worker-inference'
import type { ClassifiedEvent, VotingStatus, FlowGraphState } from '@/types'

// Edge event trigger tracking
type EdgeEventTriggers = Record<string, number>

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
  consensus: ConsensusNode,
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
      tool: true,
    },
    detailMode: true,
    focusMode: false,
    focusedNodeId: null,
  })

  // Worker detail modal state
  const [selectedWorker, setSelectedWorker] = useState<WorkerDetail | null>(null)
  const [isWorkerModalOpen, setIsWorkerModalOpen] = useState(false)

  const openWorkerDetail = useCallback((worker: WorkerDetail) => {
    setSelectedWorker(worker)
    setIsWorkerModalOpen(true)
  }, [])

  const closeWorkerDetail = useCallback(() => {
    setIsWorkerModalOpen(false)
    setSelectedWorker(null)
  }, [])

  // Coordinator detail modal state
  const [isCoordinatorModalOpen, setIsCoordinatorModalOpen] = useState(false)

  const openCoordinatorDetail = useCallback(() => {
    setIsCoordinatorModalOpen(true)
  }, [])

  const closeCoordinatorDetail = useCallback(() => {
    setIsCoordinatorModalOpen(false)
  }, [])

  // ============================================================
  //  Event-driven edge animation triggers
  //  Animation direction follows actual event flow:
  //  - Worker events (has worker_id): Worker → Consensus
  //  - Coordinator events (no worker_id): Coordinator → Workers
  // ============================================================
  const [edgeEventTriggers, setEdgeEventTriggers] = useState<EdgeEventTriggers>({})
  const prevEventsLengthRef = useRef(0)
  
  // ============================================================
  //  Infer worker count from events for animation targeting
  //  CRITICAL: Only count workers from the LATEST run_id
  //  This ensures animation targets match actual displayed workers
  // ============================================================
  const inferredWorkerCount = useMemo(() => {
    // Find latest run_id
    let latestRunId: string | undefined
    let latestRunTimestamp = 0
    
    for (const e of events) {
      const runId = e.raw.fields.run_id as string | undefined
      if (runId && e.timestamp > latestRunTimestamp) {
        latestRunId = runId
        latestRunTimestamp = e.timestamp
      }
    }

    // Filter to latest run and count workers
    const filteredEvents = latestRunId 
      ? events.filter(e => e.raw.fields.run_id === latestRunId)
      : events
    
    const workerIds = new Set<number>()
    filteredEvents.forEach(e => {
      const wid = e.raw.fields.worker_id as string | undefined
      if (wid) {
        const match = wid.match(/worker[-_]?(\d+)/i)
        if (match) workerIds.add(parseInt(match[1], 10))
      }
    })
    return Math.max(workerIds.size, votingStatus?.workers?.length || 0, 3)
  }, [events, votingStatus?.workers?.length])

  // Watch for new events and trigger edge animations
  useEffect(() => {
    if (events.length > prevEventsLengthRef.current) {
      const newEvents = events.slice(prevEventsLengthRef.current)
      
      newEvents.forEach(event => {
        const workerId = event.raw.fields.worker_id as string | undefined
        
        // ============================================================
        //  WORKER EVENTS (has worker_id) → Animate Worker → Consensus
        //  These are events produced BY workers (proposal, streaming, etc.)
        // ============================================================
        if (workerId) {
          const match = workerId.match(/worker[-_]?(\d+)/i)
          if (match) {
            const workerIndex = parseInt(match[1], 10)
            
            // Worker → Consensus for proposal/vote events
            if (event.category === 'proposal' || event.category === 'vote') {
              const edgeId = `worker-consensus-${workerIndex}`
              setEdgeEventTriggers(prev => ({
                ...prev,
                [edgeId]: (prev[edgeId] || 0) + 1,
              }))
            }
            
            // Worker → Tool for tool_call events
            if (event.category === 'tool_call') {
              const toolName = event.raw.fields.tool_name as string | undefined
              if (toolName) {
                const edgeId = `worker-tool-${workerIndex}-${toolName}`
                setEdgeEventTriggers(prev => ({
                  ...prev,
                  [edgeId]: (prev[edgeId] || 0) + 1,
                }))
              }
            }
          }
        }
        // ============================================================
        //  COORDINATOR EVENTS (no worker_id) → Animate Coordinator → Workers
        //  These are coordination events (voting rounds, task distribution)
        // ============================================================
        else {
          // Voting round start or consensus-related events → broadcast to all workers
          if (event.category === 'consensus' || event.category === 'vote') {
            const phase = event.raw.phase as string | undefined
            // Only trigger on "start" phases, not results
            if (phase?.includes('start') || phase?.includes('voting') || phase === 'task_starting') {
              // Trigger animation on ALL Coordinator → Worker edges
              for (let i = 0; i < inferredWorkerCount; i++) {
                const edgeId = `coord-worker-${i}`
                // Stagger animations for visual effect
                setTimeout(() => {
                  setEdgeEventTriggers(prev => ({
                    ...prev,
                    [edgeId]: (prev[edgeId] || 0) + 1,
                  }))
                }, i * 100)
              }
            }
          }
        }
      })
    }
    prevEventsLengthRef.current = events.length
  }, [events, inferredWorkerCount])

  // Check if we have any data
  const hasData = events.length > 0 || votingStatus !== null

  // Check if we need to infer workers from events
  const isInferred = useMemo(() => {
    return needsWorkerInference(votingStatus, events)
  }, [votingStatus, events])

  // Build effective voting status (with worker inference if needed)
  const effectiveVotingStatus = useMemo(() => {
    if (!hasData) return null
    // If we have real worker data, use it; otherwise infer from events
    if (isInferred) {
      return buildInferredVotingStatus(events, votingStatus)
    }
    return votingStatus
  }, [votingStatus, events, hasData, isInferred])

  // Build nodes and edges from events (only when we have data)
  // Layout: Coordinator (top) → Workers (middle) → Consensus (bottom)
  // This reflects the actual Maker workflow: task distribution → proposals → voting
  const { initialNodes, initialEdges } = useMemo(() => {
    const nodes: Node[] = []
    const edges: Edge[] = []
    const detailMode = flowState.detailMode

    // No data - return empty graph
    if (!hasData) {
      return { initialNodes: nodes, initialEdges: edges }
    }

    // Layout constants
    const centerX = 400
    const coordinatorY = detailMode ? 50 : 30
    const workerY = detailMode ? 200 : 120
    const consensusY = detailMode ? 380 : 220
    const workerSpacing = detailMode ? 180 : 100
    const toolSpacing = detailMode ? 120 : 70

    // Count events for status indicators
    const voteEventCount = events.filter(e => e.category === 'consensus' || e.category === 'vote').length

    // ============================================================
    //  Layer 1: Coordinator Node (distributes tasks)
    //  Count coordinator events (events WITHOUT worker_id)
    // ============================================================
    const coordinatorEventCount = events.filter(e => !e.raw.fields.worker_id).length
    
    if (events.length > 0) {
      nodes.push({
        id: 'coordinator',
        type: 'coordinator',
        position: { x: centerX, y: coordinatorY },
        data: {
          label: 'Coordinator',
          status: voteEventCount > 0 ? 'running' : 'idle',
          messageCount: coordinatorEventCount,
          totalEventCount: events.length,
          detailMode,
          onClick: openCoordinatorDetail,
        },
      })
    }

    // ============================================================
    //  Layer 2: Worker Nodes (generate proposals independently)
    // ============================================================
    const workers = effectiveVotingStatus?.workers || []
    const workerCount = workers.length
    const workerStartX = centerX - ((workerCount - 1) * workerSpacing) / 2

    // Extract worker content from events (for summary preview and detail modal)
    const getWorkerContent = (workerId: string, workerIndex: number) => {
      // Find events for this worker
      const workerEvents = events.filter(e => {
        const evtWorkerId = e.raw.fields.worker_id
        if (!evtWorkerId) return false
        // Match by worker_id (e.g., "worker-0") or index
        return evtWorkerId === `worker-${workerIndex}` || 
               evtWorkerId.toLowerCase().includes(workerId.toLowerCase())
      })
      
      // Get the latest event with LLM content
      const contentEvent = workerEvents.find(e => e.llmConversation?.assistantResponse)
      
      if (contentEvent?.llmConversation) {
        const response = contentEvent.llmConversation.assistantResponse || ''
        return {
          summary: response.slice(0, 80) + (response.length > 80 ? '...' : ''),
          systemPrompt: contentEvent.llmConversation.systemPrompt,
          userPrompt: contentEvent.llmConversation.userPrompt,
          assistantResponse: response,
          proposalId: contentEvent.raw.fields.proposal_id || contentEvent.id,
          tokensUsed: contentEvent.raw.fields.tokens_used as number | undefined,
          timestamp: contentEvent.timestamp,
        }
      }
      return null
    }

    workers.forEach((worker, i) => {
      const x = workerStartX + i * workerSpacing
      const color = worker.color || WORKER_COLORS[i % WORKER_COLORS.length]
      const content = getWorkerContent(worker.id, i)
      
      nodes.push({
        id: `worker-${i}`,
        type: 'worker',
        position: { x, y: workerY },
        data: {
          label: worker.name,
          votes: worker.votes,
          isLeader: worker.isLeader,
          color,
          status: content ? 'completed' : (worker.isLeader ? 'completed' : 'idle'),
          detailMode,
          isInferred,
          // Content preview fields
          summary: content?.summary,
          hasContent: !!content,
          // Store content for modal (will be accessed via workerDetails)
          _content: content,
          _workerIndex: i,
        },
      })

      // Edge: Coordinator → Worker (task distribution)
      if (nodes.some(n => n.id === 'coordinator')) {
        const edgeId = `coord-worker-${i}`
        edges.push({
          id: edgeId,
          source: 'coordinator',
          target: `worker-${i}`,
          type: 'animated',
          animated: true,
          style: { stroke: color, strokeWidth: detailMode ? 2 : 1 },
          data: { 
            color, 
            detailMode,
            eventTrigger: edgeEventTriggers[edgeId] || 0,
            eventLabel: 'task',
          },
        })
      }
    })

    // ============================================================
    //  Layer 3: Consensus Node (cluster voting results)
    // ============================================================
    if (effectiveVotingStatus && workerCount > 0) {
      const status = effectiveVotingStatus
      nodes.push({
        id: 'consensus',
        type: 'consensus',
        position: { x: centerX, y: consensusY },
        data: {
          clusterCount: status.clusterCount || 0,
          winnerVotes: status.winner?.votes || 0,
          runnerUpVotes: status.winner?.runnerUpVotes || 0,
          kValue: status.k || 2,
          consensusReached: status.consensusReached || false,
          mode: status.mode || 'semantic',
          detailMode,
        },
      })

      // Edges: Workers → Consensus (proposals flow to voting)
      workers.forEach((worker, i) => {
        const edgeId = `worker-consensus-${i}`
        const color = worker.color || WORKER_COLORS[i % WORKER_COLORS.length]
        edges.push({
          id: edgeId,
          source: `worker-${i}`,
          target: 'consensus',
          type: 'animated',
          animated: true,
          style: { 
            stroke: worker.isLeader ? '#22c55e' : color, 
            strokeWidth: worker.isLeader ? (detailMode ? 3 : 2) : (detailMode ? 1.5 : 1),
            strokeDasharray: worker.isLeader ? undefined : '5 5',
          },
          data: { 
            color: worker.isLeader ? '#22c55e' : color, 
            detailMode,
            eventTrigger: edgeEventTriggers[edgeId] || 0,
            eventLabel: worker.isLeader ? 'leader' : 'proposal',
          },
        })
      })
    }

    // ============================================================
    //  Side Panel: Tool Nodes (external tools called by workers)
    // ============================================================
    const toolEvents = events.filter(e => e.category === 'tool_call')
    const toolNames = [...new Set(toolEvents.map(e => e.toolInfo?.toolName).filter(Boolean))]
    
    // Position tools on the left side
    const toolStartY = workerY - (toolNames.length - 1) * toolSpacing / 2
    
    toolNames.forEach((toolName, i) => {
      if (!toolName) return
      const callCount = toolEvents.filter(e => e.toolInfo?.toolName === toolName).length
      const y = toolStartY + i * toolSpacing
      
      nodes.push({
        id: `tool-${toolName}`,
        type: 'tool',
        position: { x: 80, y },
        data: {
          label: toolName,
          callCount,
          status: 'idle',
          detailMode,
        },
      })

      // Find which worker called this tool (from events)
      const toolEvent = toolEvents.find(e => e.toolInfo?.toolName === toolName)
      const workerId = toolEvent?.raw.fields.worker_id
      
      // Connect to the worker that called this tool, or first worker
      let sourceWorker = nodes.find(n => n.id.startsWith('worker-'))
      if (workerId) {
        const workerIndex = workers.findIndex(w => 
          w.id.includes(workerId) || w.name.toLowerCase().includes(workerId.split('-').slice(-1)[0])
        )
        if (workerIndex >= 0) {
          sourceWorker = nodes.find(n => n.id === `worker-${workerIndex}`)
        }
      }
      
      if (sourceWorker) {
        edges.push({
          id: `tool-${toolName}-edge`,
          source: sourceWorker.id,
          target: `tool-${toolName}`,
          type: 'animated',
          style: { stroke: '#f43f5e', strokeWidth: detailMode ? 1 : 0.5, strokeDasharray: '4 4' },
          data: { color: '#f43f5e', detailMode },
        })
      }
    })

    return { initialNodes: nodes, initialEdges: edges }
  }, [events, effectiveVotingStatus, hasData, flowState.detailMode, isInferred, edgeEventTriggers])

  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges)
  
  // Sync nodes/edges when data changes (important for real-time updates)
  useEffect(() => {
    setNodes(initialNodes)
    setEdges(initialEdges)
  }, [initialNodes, initialEdges, setNodes, setEdges])

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
  const toggleLayer = useCallback((layer: 'maker' | 'tool') => {
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
      visibleLayers: { maker: true, tool: true },
      detailMode: true,
      focusMode: false,
      focusedNodeId: null,
    })
  }, [])

  // Handle node click - open detail modal for workers/coordinator
  const handleNodeClick = useCallback((_event: React.MouseEvent, node: Node) => {
    // Coordinator node click - open coordinator detail modal
    if (node.type === 'coordinator') {
      openCoordinatorDetail()
      return
    }
    
    // Worker node click - open worker detail modal
    if (node.type === 'worker') {
      const nodeData = node.data as {
        label: string
        color: string
        isLeader: boolean
        status: string
        _content?: {
          summary?: string
          systemPrompt?: string
          userPrompt?: string
          assistantResponse?: string
          proposalId?: string
          tokensUsed?: number
          timestamp?: number
        }
        _workerIndex: number
      }
      
      if (nodeData._content) {
        openWorkerDetail({
          id: node.id,
          name: nodeData.label,
          color: nodeData.color,
          isLeader: nodeData.isLeader,
          status: nodeData.status as 'idle' | 'running' | 'completed',
          proposalId: nodeData._content.proposalId,
          summary: nodeData._content.summary,
          systemPrompt: nodeData._content.systemPrompt,
          userPrompt: nodeData._content.userPrompt,
          assistantResponse: nodeData._content.assistantResponse,
          tokensUsed: nodeData._content.tokensUsed,
          timestamp: nodeData._content.timestamp,
          workerIndex: nodeData._workerIndex,  // Pass worker index for event filtering
        })
      }
    }
  }, [openWorkerDetail, openCoordinatorDetail])

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
        onNodeClick={handleNodeClick}
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
        {/* Controls - Bottom Left, themed */}
        <Controls
          className="!bg-[#141820]/95 !border-[rgba(125,211,252,0.2)] !shadow-lg !rounded-lg !gap-0.5 !p-1
                     [&>button]:!bg-[#1e293b] [&>button]:!border-[rgba(125,211,252,0.15)]
                     [&>button]:!text-[#94a3b8] [&>button:hover]:!bg-[#0f172a]
                     [&>button:hover]:!text-[#7dd3fc] [&>button:hover]:!border-[rgba(125,211,252,0.4)]
                     [&>button]:!rounded [&>button]:!transition-all [&>button]:!duration-200
                     [&>button]:!w-7 [&>button]:!h-7 [&>button>svg]:!w-3.5 [&>button>svg]:!h-3.5"
          showZoom
          showFitView
          showInteractive={false}
          position="bottom-left"
        />
        {/* MiniMap - Bottom Right, themed compact */}
        <MiniMap
          className="!bg-[#141820]/90 !border-[rgba(125,211,252,0.15)] !rounded-lg !shadow-lg !overflow-hidden"
          style={{ width: 100, height: 70 }}
          nodeColor={(node): string => {
            if (node.type === 'coordinator') return '#7dd3fc'  // neon-cyan
            if (node.type === 'worker') return '#86efac'       // neon-green
            if (node.type === 'tool') return '#fca5a5'         // neon-red
            if (node.type === 'consensus') return '#86efac'    // neon-green
            return '#475569'                                    // slate-600
          }}
          nodeStrokeWidth={0}
          nodeBorderRadius={2}
          maskColor="rgba(10, 10, 18, 0.9)"
          position="bottom-right"
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
      {effectiveVotingStatus && (
        <VotingStatusPanel
          status={effectiveVotingStatus}
          isInferred={isInferred}
          className="absolute top-4 left-4"
        />
      )}

      {/* Worker Detail Modal */}
      <WorkerDetailModal
        worker={selectedWorker}
        isOpen={isWorkerModalOpen}
        onClose={closeWorkerDetail}
        events={events}
      />

      {/* Coordinator Detail Modal */}
      <CoordinatorDetailModal
        isOpen={isCoordinatorModalOpen}
        onClose={closeCoordinatorDetail}
        events={events}
        totalEventCount={events.length}
      />
    </div>
  )
}

export default AgentFlowGraph
