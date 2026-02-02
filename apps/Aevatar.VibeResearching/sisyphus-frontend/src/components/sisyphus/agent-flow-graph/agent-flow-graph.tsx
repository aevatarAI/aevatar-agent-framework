// ============================================================
//  Agent Flow Graph - Main Component
//  Visualizes Vibe Research Agent topology with React Flow
//  Supports API fetch + SSE real-time updates
// ============================================================

import { useMemo, useCallback, useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import {
  ReactFlow,
  Background,
  BackgroundVariant,
  Controls,
  useNodesState,
  useEdgesState,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import './styles.css'

import AgentNode, { type AgentNodeData } from './agent-node'
import AnimatedEdge, { type AnimatedEdgeData } from './animated-edge'
import { 
  useAgentTopologyStore, 
  selectTopology, 
  selectAgentStatus,
  selectAgentStats,
  selectEdgeStats,
  selectIsLoading,
  type AgentStatus,
  type AgentStats,
} from '@/store/agent-topology-store'
import { useStreamContentStore } from '@/store/stream-content-store'

// ------------------------------------------------------------
//  Node Types Registration
// ------------------------------------------------------------

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const nodeTypes: Record<string, any> = {
  agentNode: AgentNode,
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const edgeTypes: Record<string, any> = {
  animatedEdge: AnimatedEdge,
}

// ------------------------------------------------------------
//  DAG Auto Layout Algorithm
//  Computes node positions based on topological layers
// ------------------------------------------------------------

interface LayoutPosition {
  x: number
  y: number
}

interface TopologyData {
  nodes: Array<{ id: string; type: string }>
  edges: Array<{ from: string; to: string }>
}

// Layout constants (spread out for better visualization)
const LAYOUT_CONFIG = {
  containerWidth: 500,   // Wider container for horizontal spread
  layerGap: 120,         // Vertical gap between layers
  nodeGap: 140,          // Horizontal gap between nodes in same layer
  paddingTop: 40,        // Top padding
  paddingLeft: 80,       // Left padding (minimum x)
  horizontalOffset: 30,  // Horizontal offset per edge to avoid overlap
  zigzagOffset: 80,      // Zigzag offset for single-node layers
}

/**
 * Compute DAG layers using topological sort (Kahn's algorithm)
 * Returns: nodeId -> layer index (0 = root)
 */
function computeLayers(topology: TopologyData): Map<string, number> {
  const layers = new Map<string, number>()
  const inDegree = new Map<string, number>()
  const adjacency = new Map<string, string[]>()
  
  // Initialize in-degree and adjacency list
  for (const node of topology.nodes) {
    inDegree.set(node.id, 0)
    adjacency.set(node.id, [])
  }
  
  for (const edge of topology.edges) {
    const currentDegree = inDegree.get(edge.to) ?? 0
    inDegree.set(edge.to, currentDegree + 1)
    
    const adj = adjacency.get(edge.from) ?? []
    adj.push(edge.to)
    adjacency.set(edge.from, adj)
  }
  
  // BFS to assign layers
  const queue: string[] = []
  
  // Start with nodes that have no incoming edges (in-degree = 0)
  for (const node of topology.nodes) {
    if ((inDegree.get(node.id) ?? 0) === 0) {
      queue.push(node.id)
      layers.set(node.id, 0)
    }
  }
  
  while (queue.length > 0) {
    const nodeId = queue.shift()!
    const currentLayer = layers.get(nodeId) ?? 0
    
    for (const neighbor of adjacency.get(nodeId) ?? []) {
      // Update neighbor's layer to be at least current + 1
      const existingLayer = layers.get(neighbor)
      const newLayer = currentLayer + 1
      
      if (existingLayer === undefined || newLayer > existingLayer) {
        layers.set(neighbor, newLayer)
      }
      
      // Decrease in-degree and add to queue if ready
      const newDegree = (inDegree.get(neighbor) ?? 1) - 1
      inDegree.set(neighbor, newDegree)
      
      if (newDegree === 0) {
        queue.push(neighbor)
      }
    }
  }
  
  return layers
}

interface LayoutResult {
  positions: Map<string, LayoutPosition>
  layers: Map<string, number>
}

/**
 * Compute positions and layers for all nodes
 */
function computeLayout(topology: TopologyData): LayoutResult {
  const layers = computeLayers(topology)
  const positions = new Map<string, LayoutPosition>()
  
  // Group nodes by layer
  const nodesByLayer = new Map<number, string[]>()
  for (const node of topology.nodes) {
    const layer = layers.get(node.id) ?? 0
    const nodesInLayer = nodesByLayer.get(layer) ?? []
    nodesInLayer.push(node.id)
    nodesByLayer.set(layer, nodesInLayer)
  }
  
  // Calculate positions for each layer
  const maxLayer = Math.max(...Array.from(layers.values()), 0)
  
  for (let layer = 0; layer <= maxLayer; layer++) {
    const nodesInLayer = nodesByLayer.get(layer) ?? []
    const count = nodesInLayer.length
    
    // Center the nodes horizontally
    const totalWidth = (count - 1) * LAYOUT_CONFIG.nodeGap
    const baseX = (LAYOUT_CONFIG.containerWidth - totalWidth) / 2
    
    // Add zigzag offset for single-node layers to create visual spread
    const zigzag = count === 1 ? (layer % 2 === 0 ? -LAYOUT_CONFIG.zigzagOffset : LAYOUT_CONFIG.zigzagOffset) : 0
    
    nodesInLayer.forEach((nodeId, index) => {
      positions.set(nodeId, {
        x: Math.max(LAYOUT_CONFIG.paddingLeft, baseX + index * LAYOUT_CONFIG.nodeGap + zigzag),
        y: LAYOUT_CONFIG.paddingTop + layer * LAYOUT_CONFIG.layerGap,
      })
    })
  }
  
  return { positions, layers }
}

/**
 * Get position for a node
 */
function getPosition(
  nodeId: string,
  index: number,
  positionsMap: Map<string, LayoutPosition>
): LayoutPosition {
  const cached = positionsMap.get(nodeId)
  if (cached) return cached
  
  // Fallback: grid layout for nodes not in topology
  return {
    x: (index % 3) * LAYOUT_CONFIG.nodeGap + LAYOUT_CONFIG.paddingLeft,
    y: Math.floor(index / 3) * LAYOUT_CONFIG.layerGap + LAYOUT_CONFIG.paddingTop,
  }
}

/**
 * Get layer for a node
 */
function getLayer(nodeId: string, layersMap: Map<string, number>): number {
  return layersMap.get(nodeId) ?? 0
}

// ------------------------------------------------------------
//  Fullscreen Button Component
// ------------------------------------------------------------

interface FullscreenButtonProps {
  isFullscreen: boolean
  onToggle: () => void
}

const FullscreenButton: React.FC<FullscreenButtonProps> = ({ isFullscreen, onToggle }) => (
  <button
    onClick={onToggle}
    className="size-7 rounded-lg bg-bg-surface/80 hover:bg-bg-elevated border border-border-subtle 
               flex items-center justify-center transition-colors group"
    title={isFullscreen ? 'Exit Fullscreen' : 'Enter Fullscreen'}
  >
    {isFullscreen ? (
      <svg className="size-3.5 text-text-muted group-hover:text-text-secondary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 9V4.5M9 9H4.5M9 9L3.75 3.75M9 15v4.5M9 15H4.5M9 15l-5.25 5.25M15 9h4.5M15 9V4.5M15 9l5.25-5.25M15 15h4.5M15 15v4.5m0-4.5l5.25 5.25" />
      </svg>
    ) : (
      <svg className="size-3.5 text-text-muted group-hover:text-text-secondary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3.75 3.75v4.5m0-4.5h4.5m-4.5 0L9 9M3.75 20.25v-4.5m0 4.5h4.5m-4.5 0L9 15M20.25 3.75h-4.5m4.5 0v4.5m0-4.5L15 9m5.25 11.25h-4.5m4.5 0v-4.5m0 4.5L15 15" />
      </svg>
    )}
  </button>
)

// ------------------------------------------------------------
//  Empty State Component
// ------------------------------------------------------------

const LoadingState = () => (
  <div className="h-full flex flex-col items-center justify-center text-center p-8">
    <div className="size-16 rounded-2xl bg-neon-cyan/10 flex items-center justify-center mb-4 animate-pulse">
      <svg className="size-8 text-neon-cyan animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
      </svg>
    </div>
    <h4 className="text-sm font-medium text-text-secondary mb-2">Loading Topology...</h4>
    <p className="text-xs text-text-muted max-w-xs">
      Fetching agent mesh configuration
    </p>
  </div>
)

const EmptyState = () => (
  <div className="h-full flex flex-col items-center justify-center text-center p-8">
    <div className="size-16 rounded-2xl bg-bg-surface/50 flex items-center justify-center mb-4">
      <svg className="size-8 text-text-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 17V7m0 10a2 2 0 01-2 2H5a2 2 0 01-2-2V7a2 2 0 012-2h2a2 2 0 012 2m0 10a2 2 0 002 2h2a2 2 0 002-2M9 7a2 2 0 012-2h2a2 2 0 012 2m0 10V7m0 10a2 2 0 002 2h2a2 2 0 002-2V7a2 2 0 00-2-2h-2a2 2 0 00-2 2" />
      </svg>
    </div>
    <h4 className="text-sm font-medium text-text-secondary mb-2">No Topology Data</h4>
    <p className="text-xs text-text-muted max-w-xs">
      No mesh definition found. Topology will appear once a research task starts.
    </p>
  </div>
)

// ------------------------------------------------------------
//  Types for React Flow
// ------------------------------------------------------------

interface FlowNode {
  id: string
  type: string
  position: { x: number; y: number }
  data: AgentNodeData
}

interface FlowEdge {
  id: string
  source: string
  target: string
  type: string
  data: AnimatedEdgeData
}

// ------------------------------------------------------------
//  Main Component
// ------------------------------------------------------------

interface AgentFlowGraphProps {
  className?: string
  fullHeight?: boolean
  sessionId?: string
}

const AgentFlowGraph: React.FC<AgentFlowGraphProps> = ({ className, fullHeight = false, sessionId }) => {
  // Fullscreen state
  const [isFullscreen, setIsFullscreen] = useState(false)
  const toggleFullscreen = useCallback(() => setIsFullscreen(prev => !prev), [])
  
  // Store subscriptions - Topology store
  const topology = useAgentTopologyStore(selectTopology)
  const agentStatus = useAgentTopologyStore(selectAgentStatus)
  const agentStats = useAgentTopologyStore(selectAgentStats)
  const edgeStats = useAgentTopologyStore(selectEdgeStats)
  const isLoading = useAgentTopologyStore(selectIsLoading)
  const setSelectedAgent = useAgentTopologyStore(state => state.setSelectedAgent)
  
  // Subscribe to stream-content-store for real-time token counts (same as Agent Info tab)
  const agentStreams = useStreamContentStore(state => state.agentStreams)
  
  // Track if fetch was already initiated for this session
  const fetchInitiated = useRef(false)
  
  // Fetch topology on mount
  // NOTE: Don't reset store on unmount - Tab switching should preserve data
  // Session switching is handled by key={sessionId} which remounts component
  // and store.fetchTopology checks sessionId to avoid duplicate data
  useEffect(() => {
    if (!sessionId || fetchInitiated.current) return
    
    // Check if store already has data for this session (from SSE or previous fetch)
    const { topology, sessionId: storeSessionId } = useAgentTopologyStore.getState()
    if (topology && storeSessionId === sessionId) {
      fetchInitiated.current = true
      return
    }
    
    fetchInitiated.current = true
    useAgentTopologyStore.getState().fetchTopology(sessionId)
  }, [sessionId])
  
  // Compute positions and layers from topology (auto DAG layout)
  const { positions: positionsMap, layers: layersMap } = useMemo(() => {
    if (!topology) return { positions: new Map(), layers: new Map() }
    return computeLayout(topology)
  }, [topology])
  
  // Convert topology to React Flow nodes (structure only, no status)
  // This prevents position reset when status changes
  const baseNodes: FlowNode[] = useMemo(() => {
    if (!topology) return []
    return topology.nodes.map((node, index) => {
      const agentType = node.type.toLowerCase()
      const position = getPosition(node.id, index, positionsMap)
      const layer = getLayer(node.id, layersMap)
      
      return {
        id: node.id,
        type: 'agentNode',
        position,
        data: {
          label: agentType.replace(/_/g, ' '),
          agentType,
          status: 'idle' as AgentStatus, // Initial status, will be updated
          layer,
        },
      }
    })
  }, [topology, positionsMap, layersMap]) // Note: agentStatus removed
  
  // Convert topology to React Flow edges (structure only, no status)
  // Group edges by source to calculate offsets for non-overlapping paths
  const baseEdges: FlowEdge[] = useMemo(() => {
    if (!topology) return []
    
    // Count edges from each source
    const edgesBySource = new Map<string, number>()
    topology.edges.forEach(edge => {
      edgesBySource.set(edge.from, (edgesBySource.get(edge.from) ?? 0) + 1)
    })
    
    // Track current index for each source
    const sourceIndexTracker = new Map<string, number>()
    
    return topology.edges.map((edge) => {
      // Get index and total for this source (for edge offset calculation)
      const currentIndex = sourceIndexTracker.get(edge.from) ?? 0
      const totalFromSource = edgesBySource.get(edge.from) ?? 1
      sourceIndexTracker.set(edge.from, currentIndex + 1)
      
      return {
        id: `${edge.from}-${edge.to}`,
        source: edge.from,
        target: edge.to,
        type: 'animatedEdge',
        data: {
          sourceStatus: undefined, // Will be updated dynamically
          targetStatus: undefined,
          edgeIndex: currentIndex,
          totalFromSource,
        },
      }
    })
  }, [topology]) // Note: agentStatus removed
  
  // React Flow state - use setNodes/setEdges to sync when topology STRUCTURE changes
  const [nodes, setNodes, onNodesChange] = useNodesState(baseNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(baseEdges)
  
  // Track topology structure to detect changes (not status changes)
  const prevTopologyRef = useRef<string | null>(null)
  
  // Sync nodes/edges ONLY when topology structure changes (not on status updates)
  useEffect(() => {
    if (!topology) return
    
    // Create a structure signature (node ids + edge connections)
    const structureSignature = JSON.stringify({
      nodes: topology.nodes.map(n => n.id).sort(),
      edges: topology.edges.map(e => `${e.from}-${e.to}`).sort(),
    })
    
    // Only reset positions if structure actually changed
    if (prevTopologyRef.current !== structureSignature) {
      prevTopologyRef.current = structureSignature
      if (baseNodes.length > 0) {
        setNodes(baseNodes)
      }
      if (baseEdges.length > 0) {
        setEdges(baseEdges)
      }
    }
  }, [topology, baseNodes, baseEdges, setNodes, setEdges])
  
  // Handle node selection for detail view
  const handleNodeSelect = useCallback((agentType: string) => {
    setSelectedAgent(agentType)
  }, [setSelectedAgent])
  
  // Update nodes when status/stats change
  // Merge stats from both topology store and stream-content-store for consistency with Agent Info tab
  const nodesWithStatus = useMemo(() => {
    return nodes.map(node => {
      const nodeData = node.data as AgentNodeData
      const topologyStats = agentStats[nodeData.agentType]
      
      // Get real-time token count from stream-content-store (same source as Agent Info tab)
      // Try exact match first, then underscore variant
      const streamData = agentStreams[nodeData.agentType] || 
                        agentStreams[nodeData.agentType.replace(/_/g, ' ')] ||
                        agentStreams[nodeData.agentType.replace(/ /g, '_')]
      
      // Merge stats: prefer stream data for tokens (real-time), topology for timing
      // For messageCount: if we have stream content, count it as at least 1 message
      const hasContent = streamData?.content && streamData.content.length > 0
      const messageCount = topologyStats?.messageCount || (hasContent ? 1 : 0)
      
      const mergedStats: AgentStats = {
        tokens: streamData?.tokenCount || topologyStats?.tokens || 0,
        startTime: topologyStats?.startTime || null,
        endTime: topologyStats?.endTime || null,
        duration: topologyStats?.duration || null,
        messageCount,
        lastActivity: streamData?.isStreaming 
          ? 'Streaming...' 
          : streamData?.isFinal 
            ? 'Completed' 
            : topologyStats?.lastActivity || '',
      }
      
      // Extract output preview (first 30 chars, cleaned up)
      let outputPreview = ''
      if (streamData?.content) {
        // Remove markdown headers and clean up whitespace
        const cleaned = streamData.content
          .replace(/^#+\s*/gm, '')  // Remove markdown headers
          .replace(/\*\*/g, '')     // Remove bold markers
          .replace(/\s+/g, ' ')     // Normalize whitespace
          .trim()
        outputPreview = cleaned.slice(0, 30) + (cleaned.length > 30 ? '…' : '')
      }
      
      // Determine status: prefer stream-based status for consistency with Agent Info tab
      // If streaming -> 'running', if final -> 'completed', else use topology store status
      let derivedStatus: AgentStatus = agentStatus[nodeData.agentType] || 'idle'
      if (streamData?.isStreaming) {
        derivedStatus = 'running'
      } else if (streamData?.isFinal) {
        derivedStatus = 'completed'
      }
      
      return {
        ...node,
        data: {
          ...nodeData,
          status: derivedStatus,
          stats: mergedStats,
          outputPreview,
          onSelect: handleNodeSelect,
        },
      }
    })
  }, [nodes, agentStatus, agentStats, agentStreams, handleNodeSelect])
  
  // Update edges when status changes (preserve edgeIndex/totalFromSource, add message count)
  const edgesWithStatus = useMemo(() => {
    return edges.map(edge => {
      const sourceNode = nodesWithStatus.find(n => n.id === edge.source)
      const targetNode = nodesWithStatus.find(n => n.id === edge.target)
      const sourceData = sourceNode?.data as AgentNodeData | undefined
      const targetData = targetNode?.data as AgentNodeData | undefined
      const edgeData = edge.data as AnimatedEdgeData | undefined
      
      // Get edge stats for message count
      const edgeKey = `${edge.source}-${edge.target}`
      const stats = edgeStats[edgeKey]
      
      return {
        ...edge,
        data: {
          sourceStatus: sourceData?.status,
          targetStatus: targetData?.status,
          edgeIndex: edgeData?.edgeIndex,
          totalFromSource: edgeData?.totalFromSource,
          messageCount: stats?.messageCount || 0,
        },
      }
    })
  }, [edges, nodesWithStatus, edgeStats])
  
  // Disable interactions for read-only view
  const onNodeDragStop = useCallback(() => {}, [])
  
  // Graph content (reusable for normal and fullscreen modes)
  const graphContent = (
    <>
      {isLoading ? (
        <LoadingState />
      ) : !topology ? (
        <EmptyState />
      ) : (
        <ReactFlow
          nodes={nodesWithStatus}
          edges={edgesWithStatus}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onNodeDragStop={onNodeDragStop}
          fitView
          fitViewOptions={{ 
            padding: 0.3,
            minZoom: 0.8,
            maxZoom: 1.5,
          }}
          nodesDraggable={true}
          nodesConnectable={false}
          elementsSelectable={true}
          panOnDrag={true}
          zoomOnScroll={true}
          zoomOnPinch={true}
          zoomOnDoubleClick={true}
          minZoom={0.3}
          maxZoom={2.5}
          preventScrolling={true}
          proOptions={{ hideAttribution: true }}
        >
          <Background 
            variant={BackgroundVariant.Dots} 
            gap={20} 
            size={1}
            color="rgba(255, 255, 255, 0.05)"
          />
          <Controls 
            showZoom={true}
            showFitView={true}
            showInteractive={false}
            position="bottom-right"
            className="!bg-bg-surface/80 !border-border-subtle !rounded-lg !shadow-lg"
          />
        </ReactFlow>
      )}
    </>
  )
  
  // Fullscreen overlay - use Portal to render outside modal container
  if (isFullscreen) {
    return createPortal(
      <div className="fixed inset-0 z-[9999] bg-[#0a0a0f] flex flex-col agent-flow-graph">
        {/* Fullscreen Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-white/10 flex-shrink-0 bg-[#0a0a0f]">
          <div className="flex items-center gap-2">
            <div className="size-6 rounded-lg bg-cyan-500/20 flex items-center justify-center">
              <svg className="size-3.5 text-cyan-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 10V3L4 14h7v7l9-11h-7z" />
              </svg>
            </div>
            <h3 className="text-sm font-medium text-white/80 tracking-wider">AGENT TOPOLOGY</h3>
          </div>
          <FullscreenButton isFullscreen={isFullscreen} onToggle={toggleFullscreen} />
        </div>
        
        {/* Fullscreen Graph - with explicit styling for Controls */}
        <div className="flex-1 min-h-0 fullscreen-graph-container">
          {graphContent}
        </div>
        
        {/* Fullscreen-specific styles */}
        <style>{`
          .fullscreen-graph-container .react-flow__controls {
            background: rgba(20, 20, 30, 0.9) !important;
            border: 1px solid rgba(255, 255, 255, 0.1) !important;
            border-radius: 8px !important;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3) !important;
          }
          .fullscreen-graph-container .react-flow__controls-button {
            background: transparent !important;
            border: none !important;
            color: rgba(255, 255, 255, 0.6) !important;
          }
          .fullscreen-graph-container .react-flow__controls-button:hover {
            background: rgba(255, 255, 255, 0.1) !important;
            color: rgba(255, 255, 255, 0.9) !important;
          }
          .fullscreen-graph-container .react-flow__controls-button svg {
            fill: currentColor !important;
          }
        `}</style>
      </div>,
      document.body
    )
  }
  
  return (
    <div className={`agent-flow-graph relative ${fullHeight ? 'h-full flex flex-col' : ''} ${className || ''}`}>
      {/* Header */}
      <div className="flex items-center justify-between mb-3 flex-shrink-0">
        <div className="flex items-center gap-2">
          <div className="size-6 rounded-lg bg-neon-cyan/20 flex items-center justify-center">
            <svg className="size-3.5 text-neon-cyan" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
          </div>
          <h3 className="text-xs font-display font-medium text-text-muted tracking-wider">AGENT TOPOLOGY</h3>
        </div>
        <FullscreenButton isFullscreen={isFullscreen} onToggle={toggleFullscreen} />
      </div>
      
      {/* Graph Container */}
      <div 
        className={`rounded-xl border border-border-subtle bg-bg-void/50 overflow-hidden ${fullHeight ? 'flex-1 min-h-0' : ''}`}
        style={fullHeight ? undefined : { height: 380 }}
      >
        {graphContent}
      </div>
    </div>
  )
}

export default AgentFlowGraph
