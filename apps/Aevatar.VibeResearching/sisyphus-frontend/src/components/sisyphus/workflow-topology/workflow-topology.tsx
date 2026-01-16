// ============================================================
//  WorkflowTopology - Main DAG Visualization Component
// ============================================================

import { useCallback, useMemo, useEffect, useRef, useState } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  useNodesState,
  useEdgesState,
  BackgroundVariant,
  MarkerType,
  type Node,
  type Edge,
  type ReactFlowInstance,
} from '@xyflow/react'
import { GitBranch, ArrowUpCircle, ArrowDownCircle, Link2, X } from 'lucide-react'
import { useSisyphusStore } from '@/store/sisyphus-store'
import { useDagInteractions } from '@/hooks/use-dag-interactions'
import type { DAGNode, NodeKind } from '@/types'
import { getDagSnapshot } from '@/lib/axiom-client'
import { cn } from '@/lib/utils'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog'
import { SummaryModal } from '../summary-modal'
import { SubGraphViewer } from '../sub-graph-viewer'
import '@xyflow/react/dist/style.css'

import { type NodeFilterMode } from './dag-node-styles'
import { getLayoutedElements } from './dag-layout'
import { nodeTypes } from './cyber-node'
import { NodeLegend } from './node-legend'
import { NodeDetailsPanel } from './node-details-panel'
import { TopologyHeader } from './topology-header'

// ─────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────

export interface WorkflowTopologyProps {
  sessionId: string
  fullHeight?: boolean
  onCollapse?: () => void
}

// ─────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────

export function WorkflowTopology({ sessionId, fullHeight = false, onCollapse }: WorkflowTopologyProps) {
  const { dag, selectedNodeId, setDag, setSelectedNode, isConnected, activeMilestoneNodeId, setActiveMilestoneNodeId } = useSisyphusStore()
  const { setHighlight, clearHighlight, highlightMode, highlightedNodeIds, dagStats } = useDagInteractions()
  const reactFlowInstance = useRef<ReactFlowInstance | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [summaryOpen, setSummaryOpen] = useState(false)
  const [filterMode, setFilterMode] = useState<NodeFilterMode>('all')
  const [isFullscreen, setIsFullscreen] = useState(false)
  const prevActiveMilestone = useRef<string | null>(null)
  const prevSessionId = useRef<string>(sessionId)
  const isProgrammaticMove = useRef(false)
  const refreshInProgress = useRef(false)
  const prevNodeCount = useRef(0)
  const hasUserInteracted = useRef(false)

  const selectedNodeForDialog = useMemo(() => {
    if (!selectedNodeId || !dag?.nodes) return null
    return dag.nodes.find(n => n.id === selectedNodeId) || null
  }, [dag, selectedNodeId])

  // ── Focus on a specific node by ID ──
  const focusOnNode = useCallback((nodeId: string) => {
    if (!reactFlowInstance.current) return
    const node = reactFlowInstance.current.getNode(nodeId)
    if (!node) return

    isProgrammaticMove.current = true
    reactFlowInstance.current.setCenter(
      node.position.x + 24,
      node.position.y + 24,
      { zoom: 1.2, duration: 500 }
    )
    setTimeout(() => { isProgrammaticMove.current = false }, 600)
  }, [])

  const focusOnActiveMilestone = useCallback(() => {
    if (activeMilestoneNodeId) focusOnNode(activeMilestoneNodeId)
  }, [activeMilestoneNodeId, focusOnNode])

  // ── Smart Focus: prioritize Active Milestone > Plan > Knowledge > Other ──
  const smartFocus = useCallback(() => {
    if (!reactFlowInstance.current || !dag?.nodes) return

    // Priority 1: Active milestone (currently executing plan node)
    if (activeMilestoneNodeId) {
      focusOnNode(activeMilestoneNodeId)
      return
    }

    // Priority 2: Plan nodes in current session (prefer last one as "newest")
    const currentSessionPlanNodes = dag.nodes.filter(
      n => n.kind === 'Plan' && (!n.sessionId || n.sessionId === sessionId)
    )
    if (currentSessionPlanNodes.length > 0) {
      const lastPlan = currentSessionPlanNodes[currentSessionPlanNodes.length - 1]
      focusOnNode(lastPlan.id)
      return
    }

    // Priority 3: Any Plan node (from other sessions)
    const anyPlanNode = dag.nodes.find(n => n.kind === 'Plan')
    if (anyPlanNode) {
      focusOnNode(anyPlanNode.id)
      return
    }

    // Priority 4: Knowledge nodes in current session (prefer last one)
    const currentSessionKnowledgeNodes = dag.nodes.filter(
      n => n.kind === 'Knowledge' && (!n.sessionId || n.sessionId === sessionId)
    )
    if (currentSessionKnowledgeNodes.length > 0) {
      const lastKnowledge = currentSessionKnowledgeNodes[currentSessionKnowledgeNodes.length - 1]
      focusOnNode(lastKnowledge.id)
      return
    }

    // Priority 5: Any Knowledge node (from other sessions)
    const anyKnowledgeNode = dag.nodes.find(n => n.kind === 'Knowledge')
    if (anyKnowledgeNode) {
      focusOnNode(anyKnowledgeNode.id)
      return
    }

    // Priority 6: First node as fallback (Other types)
    if (dag.nodes.length > 0) {
      focusOnNode(dag.nodes[0].id)
    }
  }, [dag, activeMilestoneNodeId, sessionId, focusOnNode])

  // ── Handle filter change ──
  const handleFilterChange = useCallback((newMode: NodeFilterMode) => {
    setFilterMode(newMode)
  }, [])

  // Auto-follow active milestone (always on)
  useEffect(() => {
    if (!activeMilestoneNodeId) return
    if (activeMilestoneNodeId === prevActiveMilestone.current) return
    const timer = setTimeout(() => focusOnNode(activeMilestoneNodeId), 300)
    prevActiveMilestone.current = activeMilestoneNodeId
    return () => clearTimeout(timer)
  }, [activeMilestoneNodeId, focusOnNode])

  // Re-focus when session changes
  useEffect(() => {
    if (sessionId === prevSessionId.current) return
    prevSessionId.current = sessionId
    hasUserInteracted.current = false
    prevNodeCount.current = 0
    // Delay to allow DAG data to load for new session
    const timer = setTimeout(smartFocus, 500)
    return () => clearTimeout(timer)
  }, [sessionId, smartFocus])

  // Silent refresh DAG from API
  const silentRefresh = useCallback(async () => {
    if (!sessionId || !isConnected || refreshInProgress.current) return
    refreshInProgress.current = true
    try {
      const snapshot = await getDagSnapshot(sessionId)
      if (snapshot) {
        const nodes: DAGNode[] = (snapshot.nodes || []).map(n => ({
          id: n.id, label: n.label || n.id, status: 'completed', type: n.type || 'node',
          kind: n.kind as NodeKind | undefined, owner: n.owner, proof: n.proof,
          attestations: n.attestations, attestationsCount: n.attestationsCount, sessionId: n.sessionId,
          // Map planStatus from API for Plan nodes (Active milestone detection)
          planStatus: n.planStatus as 'Pending' | 'Active' | 'Completed' | undefined,
        }))
        const edges = (snapshot.edges || []).map(e => ({ source: e.fromId, target: e.toId, type: e.type }))
        setDag({ nodes, edges })
        
        // Detect and update active milestone from planStatus
        const activeNode = nodes.find(n => n.planStatus === 'Active')
        if (activeNode && activeNode.id !== activeMilestoneNodeId) {
          setActiveMilestoneNodeId(activeNode.id, sessionId)
        } else if (!activeNode && activeMilestoneNodeId) {
          // Clear if no active milestone anymore
          setActiveMilestoneNodeId(null, sessionId)
        }
      }
    } catch (e) {
      if ((e as Error)?.name !== 'AbortError') console.error("Failed to refresh DAG:", e)
    } finally {
      refreshInProgress.current = false
    }
  }, [sessionId, isConnected, setDag, activeMilestoneNodeId, setActiveMilestoneNodeId])

  const handleRefresh = useCallback(async () => {
    setRefreshing(true)
    await silentRefresh()
    setRefreshing(false)
  }, [silentRefresh])

  // Auto-refresh every 10 seconds (reduced from 5s for performance)
  // DAG updates are typically event-driven, polling is just a fallback
  useEffect(() => {
    if (!sessionId || !isConnected) return
    silentRefresh()
    const intervalId = setInterval(silentRefresh, 10000)
    return () => clearInterval(intervalId)
  }, [sessionId, isConnected, silentRefresh])

  // Convert graph data to ReactFlow format
  const { nodes: initialNodes, edges: initialEdges } = useMemo(() => {
    if (!dag?.nodes || dag.nodes.length === 0) return { nodes: [] as Node[], edges: [] as Edge[] }

    const isHighlighting = highlightMode !== 'none'

    const filteredDagNodes = dag.nodes.slice(0, 200).filter((node) => {
      if (filterMode === 'all') return true
      const isOtherSession = node.sessionId ? node.sessionId !== sessionId : false
      const isCurrentSession = !isOtherSession
      const isActiveMilestone = node.id === activeMilestoneNodeId
      if (filterMode === 'PlanActive') return isActiveMilestone
      if (filterMode === 'Plan') return node.kind === 'Plan' && isCurrentSession
      if (filterMode === 'Knowledge') return node.kind === 'Knowledge' && isCurrentSession
      if (filterMode === 'OtherSession') return isOtherSession
      return true
    })

    const visibleNodeIds = new Set(filteredDagNodes.map(n => n.id))

    const nodes: Node[] = filteredDagNodes.map((node) => {
      const isSelected = node.id === selectedNodeId
      const isHighlighted = highlightedNodeIds.includes(node.id)
      const isOtherSession = node.sessionId ? node.sessionId !== sessionId : false
      const isActiveMilestone = node.id === activeMilestoneNodeId
      const planStatus = isActiveMilestone ? 'Active' : (node.planStatus as 'Pending' | 'Active' | 'Completed' | undefined)
      const isDimmed = isHighlighting && !isSelected && !isHighlighted

      return {
        id: node.id,
        type: 'cyber',
        data: {
          id: node.id, label: node.label || node.id, status: node.status || 'pending',
          selected: isSelected, kind: node.kind as 'Plan' | 'Knowledge' | undefined,
          planStatus, highlighted: isHighlighted, dimmed: isDimmed, isOtherSession,
        },
        position: { x: 0, y: 0 },
      }
    })

    const filteredDagEdges = (dag.edges || []).slice(0, 400).filter((edge) => 
      visibleNodeIds.has(edge.source) && visibleNodeIds.has(edge.target)
    )

    const edges: Edge[] = filteredDagEdges.map((edge, i) => {
      const isMotivatedBy = edge.type === 'motivated_by'
      const edgeColor = isMotivatedBy ? '#f59e0b' : '#00f0ff'
      return {
        id: `e-${edge.source}-${edge.target}-${i}`,
        source: edge.source, target: edge.target, animated: true,
        style: { stroke: edgeColor, strokeWidth: isMotivatedBy ? 1.5 : 2, strokeDasharray: isMotivatedBy ? '5 3' : undefined },
        markerEnd: { type: MarkerType.ArrowClosed, color: edgeColor, width: isMotivatedBy ? 16 : 20, height: isMotivatedBy ? 16 : 20 },
        label: isMotivatedBy ? '✨' : undefined,
        labelStyle: isMotivatedBy ? { fontSize: 10 } : undefined,
      }
    })

    return getLayoutedElements(nodes, edges, 'TB')
  }, [dag, selectedNodeId, highlightMode, highlightedNodeIds, sessionId, activeMilestoneNodeId, filterMode])

  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges)

  useEffect(() => {
    setNodes(initialNodes)
    setEdges(initialEdges)
    const currentCount = initialNodes.length
    const wasEmpty = prevNodeCount.current === 0
    const nowHasNodes = currentCount > 0

    // Smart focus on initial load (0 → N transition) if no user interaction
    if (wasEmpty && nowHasNodes && !hasUserInteracted.current) {
      setTimeout(smartFocus, 100)
    }
    prevNodeCount.current = currentCount
  }, [initialNodes, initialEdges, setNodes, setEdges, smartFocus])

  const handleMoveEnd = useCallback(() => {
    if (!isProgrammaticMove.current) hasUserInteracted.current = true
  }, [])

  const onLayout = useCallback((direction: 'TB' | 'LR') => {
    const { nodes: layoutedNodes, edges: layoutedEdges } = getLayoutedElements(nodes, edges, direction)
    setNodes([...layoutedNodes])
    setEdges([...layoutedEdges])
  }, [nodes, edges, setNodes, setEdges])

  const onNodeClick = useCallback((_: unknown, node: Node) => {
    setSelectedNode(node.id)
    clearHighlight()
    setDetailsOpen(true)
  }, [setSelectedNode, clearHighlight])

  // Toggle fullscreen mode
  const toggleFullscreen = useCallback(() => {
    setIsFullscreen(prev => {
      const newValue = !prev
      // Re-layout and focus after fullscreen transition
      setTimeout(() => {
        if (reactFlowInstance.current) {
          reactFlowInstance.current.fitView({ padding: 0.2, duration: 300 })
        }
        setTimeout(smartFocus, 350)
      }, 100)
      return newValue
    })
  }, [smartFocus])

  // Handle ESC key to exit fullscreen
  useEffect(() => {
    if (!isFullscreen) return
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setIsFullscreen(false)
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [isFullscreen])

  // Track previous filterMode for detecting clear action
  const prevFilterMode = useRef<NodeFilterMode>(filterMode)
  const clearingInProgress = useRef(false)

  // Handle filterMode change (separate from nodes.length change)
  useEffect(() => {
    if (!reactFlowInstance.current) return
    if (filterMode === prevFilterMode.current) return // No actual filter change
    
    const wasFiltered = prevFilterMode.current !== 'all'
    const isClearing = filterMode === 'all' && wasFiltered
    prevFilterMode.current = filterMode

    if (isClearing) {
      // Mark that we're clearing - prevent fitView from nodes.length effect
      clearingInProgress.current = true
      // Delay to allow nodes to re-render, then smartFocus
      setTimeout(() => {
        isProgrammaticMove.current = true
        smartFocus()
        setTimeout(() => {
          isProgrammaticMove.current = false
          clearingInProgress.current = false
        }, 500)
      }, 100)
    } else {
      // Applying filter → fitView to show all filtered nodes
      setTimeout(() => {
        isProgrammaticMove.current = true
        reactFlowInstance.current?.fitView({ padding: 0.2, duration: 400 })
        setTimeout(() => { isProgrammaticMove.current = false }, 500)
      }, 50)
    }
  }, [filterMode, smartFocus])

  // Re-fit when nodes count changes (but not during clear operation)
  useEffect(() => {
    if (!reactFlowInstance.current || !nodes.length) return
    if (clearingInProgress.current) return // Skip during clear
    if (filterMode === 'all') return // Don't fitView when showing all
    
    setTimeout(() => {
      isProgrammaticMove.current = true
      reactFlowInstance.current?.fitView({ padding: 0.2, duration: 400 })
      setTimeout(() => { isProgrammaticMove.current = false }, 500)
    }, 50)
  }, [nodes.length, filterMode])

  const nodeCount = dag?.nodes?.length ?? 0
  const edgeCount = dag?.edges?.length ?? 0

  // Empty state
  if (!dag?.nodes || dag.nodes.length === 0) {
    return (
      <div className={cn("card flex flex-col", fullHeight && "h-full")}>
        <TopologyHeader onLayout={onLayout} onRefresh={handleRefresh} refreshing={refreshing} nodeCount={0} edgeCount={0} activeMilestone={activeMilestoneNodeId} />
        <div className="flex-1 flex flex-col items-center justify-center py-8 text-center min-h-[300px]">
          <div className="relative">
            <div className="absolute inset-0 rounded-full bg-accent-emerald blur-2xl opacity-20 animate-pulse" />
            <div className="relative flex h-20 w-20 items-center justify-center rounded-full border-2 border-border bg-surface">
              <GitBranch className="h-10 w-10 text-text-muted" />
            </div>
          </div>
          <p className="mt-6 text-base text-text-muted font-mono">AWAITING TOPOLOGY DATA</p>
          <p className="mt-2 text-xs text-text-dimmed font-mono max-w-xs">Graph will materialize as reasoning progresses</p>
          {isConnected && (
            <button onClick={handleRefresh} disabled={refreshing} className="mt-4 px-4 py-2 text-xs font-mono rounded-lg border border-neon-cyan/40 text-neon-cyan hover:bg-neon-cyan/10 transition-colors disabled:opacity-50">
              {refreshing ? "Loading..." : "Load DAG"}
            </button>
          )}
        </div>
      </div>
    )
  }

  return (
    <div className={cn(
      "flex flex-col overflow-hidden transition-all duration-300",
      isFullscreen 
        ? "fixed inset-0 z-[9999] bg-[#0c0f14]" 
        : "card",
      fullHeight && !isFullscreen && "h-full"
    )}>
      {/* Header - always on top */}
      <div className="relative z-30 flex-shrink-0 bg-[#0c0f14]">
        <TopologyHeader
          onLayout={onLayout} onRefresh={handleRefresh} onCollapse={isFullscreen ? undefined : onCollapse}
          onSummary={() => setSummaryOpen(true)}
          onFullscreenToggle={toggleFullscreen} isFullscreen={isFullscreen}
          refreshing={refreshing} nodeCount={nodeCount} edgeCount={edgeCount}
          planCount={dagStats.planCount} knowledgeCount={dagStats.knowledgeCount}
        />
      </div>

      {/* ReactFlow container */}
      <div className={cn("flex-1 relative z-10", isFullscreen ? "min-h-0" : "min-h-[350px]")}>
        <div className="absolute inset-0 bg-[#0c0f14]">
          <ReactFlow
            nodes={nodes} edges={edges} onNodesChange={onNodesChange} onEdgesChange={onEdgesChange}
            onNodeClick={onNodeClick} onMoveEnd={handleMoveEnd} nodeTypes={nodeTypes}
            fitView={false} fitViewOptions={{ padding: 0.3, maxZoom: 1.5, minZoom: 0.1 }}
            defaultViewport={{ x: 0, y: 0, zoom: 1.2 }} minZoom={0.1} maxZoom={2}
            onInit={(instance) => { reactFlowInstance.current = instance }}
            proOptions={{ hideAttribution: true }} 
            style={{ background: 'transparent' }}
          >
            <Background variant={BackgroundVariant.Dots} gap={20} size={1} color="rgba(0, 255, 136, 0.15)" />
            <Controls className="!bg-bg-surface/90 !backdrop-blur-md !border !border-neon-cyan/30 !rounded-lg !shadow-none [&>button]:!bg-transparent [&>button]:!border-0 [&>button]:!border-b [&>button]:!border-border-subtle [&>button]:!text-neon-cyan [&>button]:!w-8 [&>button]:!h-8 [&>button]:!p-0 [&>button:hover]:!bg-neon-cyan/20 [&>button:last-child]:!border-b-0 [&>button>svg]:!w-4 [&>button>svg]:!h-4 [&>button>svg]:!fill-neon-cyan" position="bottom-right" />
          </ReactFlow>
          {/* Exit fullscreen button - top left corner */}
          {isFullscreen && (
            <button
              onClick={toggleFullscreen}
              className="absolute top-4 left-4 z-50 flex items-center gap-2 px-3 py-2 rounded-lg bg-bg-surface/90 backdrop-blur-md border border-neon-cyan/30 text-neon-cyan hover:bg-neon-cyan/20 transition-all font-mono text-xs"
            >
              <X className="size-4" />
              <span>EXIT FULLSCREEN</span>
            </button>
          )}
        </div>
      </div>

      {/* Footer - always on top */}
      <div className={cn("relative z-30 flex-shrink-0", isFullscreen && "bg-[#0c0f14]")}>
        <NodeLegend filterMode={filterMode} onFilterChange={handleFilterChange} />
      </div>

      {/* Node details modal - Split Panel Layout with SubGraph + Details */}
      <Dialog open={detailsOpen} onOpenChange={(open) => { setDetailsOpen(open); if (!open) { setSelectedNode(null); clearHighlight() } }}>
        <DialogContent className="bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-xl shadow-lg text-text-primary overflow-hidden max-w-7xl w-[90vw]">
          <DialogHeader>
            <div className="min-w-0">
              <DialogTitle>Node details</DialogTitle>
              <DialogDescription>
                {selectedNodeForDialog?.id ? <span className="font-mono tabular-nums">{selectedNodeForDialog.id}</span> : <span className="font-mono">—</span>}
              </DialogDescription>
            </div>
            <DialogCloseButton />
          </DialogHeader>

          {/* Split Panel Layout */}
          <div className="flex min-h-[450px] max-h-[70vh]">
            {/* Left Panel - Sub-graph Visualization */}
            <div className="flex-1 min-w-[300px] max-w-[400px] border-r border-border-subtle">
              {selectedNodeId && dag && (
                <SubGraphViewer
                  dag={dag}
                  selectedNodeId={selectedNodeId}
                  selectedNodeKind={(selectedNodeForDialog?.kind as 'Plan' | 'Knowledge') || 'Knowledge'}
                  nodeExplanation={useSisyphusStore.getState().nodeExplanation}
                  onNodeSelect={(nodeId) => setSelectedNode(nodeId)}
                />
              )}
            </div>

            {/* Right Panel - Details & Highlight Controls */}
            <div className="flex-[1.5] min-w-[350px] overflow-y-auto">
              <div className="p-4">
                <NodeDetailsPanel sessionId={sessionId} />
                {selectedNodeId && (
                  <div className="mt-4 pt-4 border-t border-border-subtle">
                    <div className="text-xs text-text-muted mb-2">Highlight related nodes:</div>
                    <div className="flex flex-wrap gap-2">
                      <button onClick={() => setHighlight(selectedNodeId, 'upstream')} className={cn("flex items-center gap-1.5 px-3 py-1.5 text-xs font-mono rounded-md border transition-all", highlightMode === 'upstream' ? "bg-neon-cyan/20 border-neon-cyan/40 text-neon-cyan" : "border-border-subtle text-text-muted hover:text-text-secondary hover:border-border-default")}>
                        <ArrowUpCircle className="size-3" />Upstream
                      </button>
                      <button onClick={() => setHighlight(selectedNodeId, 'downstream')} className={cn("flex items-center gap-1.5 px-3 py-1.5 text-xs font-mono rounded-md border transition-all", highlightMode === 'downstream' ? "bg-neon-gold/20 border-neon-gold/40 text-neon-gold" : "border-border-subtle text-text-muted hover:text-text-secondary hover:border-border-default")}>
                        <ArrowDownCircle className="size-3" />Downstream
                      </button>
                      <button onClick={() => setHighlight(selectedNodeId, 'chain')} className={cn("flex items-center gap-1.5 px-3 py-1.5 text-xs font-mono rounded-md border transition-all", highlightMode === 'chain' ? "bg-neon-green/20 border-neon-green/40 text-neon-green" : "border-border-subtle text-text-muted hover:text-text-secondary hover:border-border-default")}>
                        <Link2 className="size-3" />Full Chain
                      </button>
                      {highlightMode !== 'none' && (
                        <button onClick={clearHighlight} className="px-3 py-1.5 text-xs font-mono rounded-md border border-border-subtle text-text-muted hover:text-neon-rose hover:border-neon-rose/40 transition-all">Clear</button>
                      )}
                    </div>
                    {highlightMode !== 'none' && (
                      <div className="mt-2 text-[10px] text-text-dimmed">Highlighting {highlightedNodeIds.length} {highlightMode} node(s)</div>
                    )}
                  </div>
                )}
              </div>
            </div>
          </div>
        </DialogContent>
      </Dialog>

      <SummaryModal open={summaryOpen} onOpenChange={setSummaryOpen} sessionId={sessionId} />
    </div>
  )
}

export default WorkflowTopology
