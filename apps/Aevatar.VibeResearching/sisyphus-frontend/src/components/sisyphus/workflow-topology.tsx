import { useCallback, useMemo, useEffect, useRef, useState } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  Handle,
  useNodesState,
  useEdgesState,
  BackgroundVariant,
  Position,
  MarkerType,
  type Node,
  type Edge,
} from '@xyflow/react'
import dagre from 'dagre'
import { Network, GitBranch, RefreshCw, FileText } from 'lucide-react'
import { useSisyphusStore } from '@/store/sisyphus-store'
import { useDagInteractions } from '@/hooks/use-dag-interactions'
import type { DAGNode, NodeKind } from '@/types'
import { getDagSnapshot, getNodeExplanation } from '@/lib/axiom-client'
import { MarkdownPreview } from '@/components/ui/markdown-preview'
import { cn } from '@/lib/utils'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog'
import { SummaryModal } from './summary-modal'
import { SubGraphViewer } from './sub-graph-viewer'
import '@xyflow/react/dist/style.css'

// ============================================================
//  Workflow Topology · Enhanced DAG Visualization
// ============================================================

interface WorkflowTopologyProps {
  sessionId: string
  fullHeight?: boolean
  onCollapse?: () => void
}

// ─────────────────────────────────────────────────────────────
// Node Style (Kind-Based: US3/FR-007/FR-008)
// ─────────────────────────────────────────────────────────────

// Plan nodes = Blue/Cyan, Knowledge nodes = Green, Other session nodes = Dimmed
// Active milestone = Orange (highly visible)
const NODE_STYLES = {
  Plan: {
    bg: '#3b82f6',      // Blue-500
    border: '#60a5fa',  // Blue-400
    glow: 'rgba(59, 130, 246, 0.6)',
  },
  PlanActive: {
    bg: '#f97316',      // Orange-500
    border: '#fb923c',  // Orange-400
    glow: 'rgba(249, 115, 22, 0.8)',
  },
  Knowledge: {
    bg: '#22c55e',      // Green-500
    border: '#4ade80',  // Green-400
    glow: 'rgba(34, 197, 94, 0.6)',
  },
  // Other session nodes - dimmed purple/gray
  KnowledgeOther: {
    bg: '#6b7280',      // Gray-500
    border: '#9ca3af',  // Gray-400
    glow: 'rgba(107, 114, 128, 0.4)',
  },
  PlanOther: {
    bg: '#4b5563',      // Gray-600
    border: '#6b7280',  // Gray-500
    glow: 'rgba(75, 85, 99, 0.4)',
  },
  Default: {
    bg: '#00f0ff',
    border: '#33f4ff',
    glow: 'rgba(0, 240, 255, 0.6)',
  },
}

const STATUS_OPACITY: Record<string, number> = {
  pending: 0.6,
  running: 0.85,
  completed: 1,
  error: 0.4,
}

// ─────────────────────────────────────────────────────────────
// Custom Node Component
// ─────────────────────────────────────────────────────────────

interface CyberNodeData {
  id: string
  label: string
  status: string
  selected?: boolean
  kind?: 'Plan' | 'Knowledge'
  planStatus?: 'Pending' | 'Active' | 'Completed'
  highlighted?: boolean
  dimmed?: boolean
  isOtherSession?: boolean  // Node from a different session
}

function CyberNode({ data }: { data: CyberNodeData }) {
  const [showTooltip, setShowTooltip] = useState(false)

  // Check if this is an active plan node (pulsing) - determines style
  const isPulsing = data.kind === 'Plan' && data.planStatus === 'Active'

  // Get style based on node kind, session ownership, and active state
  const nodeStyle = data.isOtherSession
    ? (data.kind === 'Plan' ? NODE_STYLES.PlanOther : NODE_STYLES.KnowledgeOther)
    : isPulsing
      ? NODE_STYLES.PlanActive  // Orange for active milestone
      : data.kind === 'Plan'
        ? NODE_STYLES.Plan
        : data.kind === 'Knowledge'
          ? NODE_STYLES.Knowledge
          : NODE_STYLES.Default

  // Calculate opacity based on various states
  let opacity = STATUS_OPACITY[data.status] || 1
  if (data.dimmed) opacity = 0.3
  if (data.highlighted || data.selected) opacity = 1

  return (
    <>
      <Handle
        type="target"
        position={Position.Top}
        className="!w-2 !h-2 !bg-transparent !border-0"
      />

      <div
        className={cn(
          "relative flex items-center justify-center rounded-full transition-all duration-300 hover:scale-110 cursor-pointer",
          data.selected && "ring-2 ring-neon-gold ring-offset-2 ring-offset-bg-base",
          data.highlighted && !data.selected && "ring-2 ring-white/50 ring-offset-1 ring-offset-bg-base",
          isPulsing && "animate-glow-pulse"
        )}
        style={{
          width: 48,
          height: 48,
          background: `radial-gradient(circle, ${nodeStyle.bg} 0%, ${nodeStyle.border} 100%)`,
          border: `2px solid ${data.selected ? '#ffd700' : nodeStyle.border}`,
          boxShadow: data.selected
            ? `0 0 24px rgba(255,215,0,0.5), inset 0 0 10px rgba(255,255,255,0.2)`
            : isPulsing
              ? `0 0 30px ${nodeStyle.glow}, 0 0 60px ${nodeStyle.glow}, inset 0 0 10px rgba(255,255,255,0.2)`
              : `0 0 20px ${nodeStyle.glow}, inset 0 0 10px rgba(255,255,255,0.2)`,
          opacity,
        }}
        onMouseEnter={() => setShowTooltip(true)}
        onMouseLeave={() => setShowTooltip(false)}
      >
        {/* Kind indicator icon */}
        {data.kind === 'Plan' && (
          <span className="text-[10px]" style={{ color: '#0a0f19' }}>📋</span>
        )}
        {data.kind === 'Knowledge' && (
          <span className="text-[10px]" style={{ color: '#0a0f19' }}>💡</span>
        )}
        {!data.kind && (
          <span
            className="font-mono font-extrabold text-[10px] tracking-wider"
            style={{
              color: '#0a0f19',
              textShadow: `0 0 2px ${nodeStyle.bg}`
            }}
          >
            {data.id.slice(0, 4)}
          </span>
        )}
      </div>

      {/* Hover Tooltip */}
      {showTooltip && (
        <div
          className="absolute z-50 pointer-events-none"
          style={{
            left: '50%',
            bottom: '100%',
            transform: 'translateX(-50%)',
            marginBottom: 10,
          }}
        >
          <div
            className="px-4 py-3 rounded-lg text-xs font-mono whitespace-normal break-words"
            style={{
              width: '280px',
              background: 'rgba(10, 15, 25, 0.98)',
              border: `2px solid ${nodeStyle.border}`,
              boxShadow: `0 0 30px ${nodeStyle.glow}, 0 4px 20px rgba(0,0,0,0.5)`,
            }}
          >
            <div className="flex items-center gap-2 mb-2 pb-2 border-b border-slate-600/50">
              <span
                className="font-bold"
                style={{ color: nodeStyle.bg }}
              >
                {data.id}
              </span>
              {data.kind && (
                <span className={cn(
                  "text-[9px] px-1.5 py-0.5 rounded",
                  data.kind === 'Plan' ? "bg-blue-500/20 text-blue-400" : "bg-green-500/20 text-green-400"
                )}>
                  {data.kind}
                </span>
              )}
              {data.isOtherSession && (
                <span className="text-[9px] px-1.5 py-0.5 rounded bg-gray-500/20 text-gray-400">
                  Other Session
                </span>
              )}
              {data.planStatus && (
                <span className={cn(
                  "text-[9px] px-1.5 py-0.5 rounded",
                  data.planStatus === 'Pending' && "bg-yellow-500/20 text-yellow-400",
                  data.planStatus === 'Active' && "bg-blue-500/20 text-blue-400",
                  data.planStatus === 'Completed' && "bg-green-500/20 text-green-400"
                )}>
                  {data.planStatus}
                </span>
              )}
            </div>
            <div className="text-slate-200 leading-relaxed text-[11px]">{data.label}</div>
          </div>
          <div
            className="absolute left-1/2 -translate-x-1/2"
            style={{
              bottom: -8,
              width: 0,
              height: 0,
              borderLeft: '8px solid transparent',
              borderRight: '8px solid transparent',
              borderTop: `8px solid ${nodeStyle.border}`,
            }}
          />
        </div>
      )}

      <Handle
        type="source"
        position={Position.Bottom}
        className="!w-2 !h-2 !bg-transparent !border-0"
      />
    </>
  )
}

const nodeTypes = { cyber: CyberNode }

// ─────────────────────────────────────────────────────────────
// Dagre Layout Algorithm
// ─────────────────────────────────────────────────────────────

function getLayoutedElements(
  nodes: Node[],
  edges: Edge[],
  direction: 'TB' | 'LR' = 'TB'
): { nodes: Node[]; edges: Edge[] } {
  const dagreGraph = new dagre.graphlib.Graph()
  dagreGraph.setDefaultEdgeLabel(() => ({}))
  dagreGraph.setGraph({ rankdir: direction, nodesep: 60, ranksep: 80 })

  const nodeWidth = 48
  const nodeHeight = 48

  nodes.forEach((node) => {
    dagreGraph.setNode(node.id, { width: nodeWidth, height: nodeHeight })
  })

  edges.forEach((edge) => {
    dagreGraph.setEdge(edge.source, edge.target)
  })

  dagre.layout(dagreGraph)

  const layoutedNodes = nodes.map((node) => {
    const nodeWithPosition = dagreGraph.node(node.id)
    return {
      ...node,
      position: {
        x: nodeWithPosition.x - nodeWidth / 2,
        y: nodeWithPosition.y - nodeHeight / 2,
      },
      targetPosition: direction === 'TB' ? Position.Top : Position.Left,
      sourcePosition: direction === 'TB' ? Position.Bottom : Position.Right,
    }
  })

  return { nodes: layoutedNodes, edges }
}

// ─────────────────────────────────────────────────────────────
// Node Details Panel (Simplified - US4)
// Only shows: Node ID, Type, and Markdown Explanation from IGraphNode.Explain()
// ─────────────────────────────────────────────────────────────

interface NodeDetailsPanelProps {
  sessionId: string
}

function NodeDetailsPanel({ sessionId }: NodeDetailsPanelProps) {
  const {
    dag,
    selectedNodeId,
    nodeExplanation,
    dagLoading,
    dagError,
    setNodeExplanation,
    setDagLoading,
    setDagError,
  } = useSisyphusStore()

  const selectedNode = useMemo(() => {
    if (!selectedNodeId || !dag?.nodes) return null
    return dag.nodes.find(n => n.id === selectedNodeId)
  }, [dag, selectedNodeId])

  // Load node explanation when node selected
  useEffect(() => {
    if (!selectedNodeId || !sessionId) return

    let cancelled = false
    setDagLoading(true)
    setDagError("")

    getNodeExplanation(sessionId, selectedNodeId)
      .then((explain) => {
        if (cancelled) return
        setNodeExplanation(explain)
      })
      .catch((e) => {
        if (cancelled) return
        setDagError(e?.message || "Failed to load node details")
      })
      .finally(() => {
        if (!cancelled) setDagLoading(false)
      })

    return () => { cancelled = true }
  }, [selectedNodeId, sessionId, setNodeExplanation, setDagLoading, setDagError])

  if (!selectedNode) {
    return (
      <div className="text-xs text-text-muted text-center py-4">
        Select a node to view details
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Node Header */}
      <div>
        <div className="text-xs font-mono text-neon-cyan break-all">{selectedNode.id}</div>
        <div className="mt-1.5 flex items-center gap-2 flex-wrap">
          {/* Node Kind Badge */}
          {(nodeExplanation?.kind || selectedNode.kind) && (
            <span className={cn(
              "text-[10px] px-2 py-0.5 rounded-full font-medium",
              (nodeExplanation?.kind || selectedNode.kind) === 'Plan'
                ? "bg-blue-500/20 border border-blue-500/30 text-blue-400"
                : "bg-green-500/20 border border-green-500/30 text-green-400"
            )}>
              {nodeExplanation?.kind || selectedNode.kind}
            </span>
          )}
          {/* Title (short label) */}
          {nodeExplanation?.title && (
            <span className="text-[11px] text-text-secondary truncate max-w-[200px]">
              {nodeExplanation.title}
            </span>
          )}
        </div>
      </div>

      {/* Loading State */}
      {dagLoading && (
        <div className="text-[11px] text-text-muted animate-pulse py-4 text-center">
          Loading explanation...
        </div>
      )}

      {/* Error State */}
      {dagError && (
        <div className="text-[11px] text-neon-rose py-2">{dagError}</div>
      )}

      {/* Markdown Explanation Content */}
      {nodeExplanation?.markdownContent && (
        <MarkdownPreview
          content={nodeExplanation.markdownContent}
          title={nodeExplanation.title || selectedNode.id}
          downloadFilename={selectedNode.id}
          maxHeight="max-h-[45vh]"
        />
      )}

      {/* Dependencies Summary */}
      {nodeExplanation && (nodeExplanation.directDependencies.length > 0 || nodeExplanation.dependents.length > 0) && (
        <div className="space-y-2 pt-2 border-t border-border-subtle">
          {nodeExplanation.directDependencies.length > 0 && (
            <div className="text-[11px]">
              <span className="text-text-muted">Dependencies:</span>
              <div className="mt-1 flex flex-wrap gap-1">
                {nodeExplanation.directDependencies.slice(0, 8).map((dep) => (
                  <span key={dep} className="px-1.5 py-0.5 rounded bg-bg-elevated border border-border-subtle text-[10px] font-mono text-text-secondary">
                    {dep.length > 16 ? `${dep.slice(0, 8)}…${dep.slice(-6)}` : dep}
                  </span>
                ))}
                {nodeExplanation.directDependencies.length > 8 && (
                  <span className="text-[10px] text-text-dimmed">+{nodeExplanation.directDependencies.length - 8} more</span>
                )}
              </div>
            </div>
          )}
          {nodeExplanation.dependents.length > 0 && (
            <div className="text-[11px]">
              <span className="text-text-muted">Dependents:</span>
              <div className="mt-1 flex flex-wrap gap-1">
                {nodeExplanation.dependents.slice(0, 8).map((dep) => (
                  <span key={dep} className="px-1.5 py-0.5 rounded bg-bg-elevated border border-border-subtle text-[10px] font-mono text-text-secondary">
                    {dep.length > 16 ? `${dep.slice(0, 8)}…${dep.slice(-6)}` : dep}
                  </span>
                ))}
                {nodeExplanation.dependents.length > 8 && (
                  <span className="text-[10px] text-text-dimmed">+{nodeExplanation.dependents.length - 8} more</span>
                )}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Fallback: show label if no explanation available */}
      {!dagLoading && !nodeExplanation && selectedNode.label && (
        <div className="text-xs text-text-secondary leading-relaxed p-3 rounded-lg border border-border-subtle bg-bg-elevated/50">
          {selectedNode.label}
        </div>
      )}
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────

function WorkflowTopology({ sessionId, fullHeight = false, onCollapse }: WorkflowTopologyProps) {
  const { dag, selectedNodeId, setDag, setSelectedNode, isConnected, activeMilestoneNodeId } = useSisyphusStore()
  const { clearHighlight, highlightMode, highlightedNodeIds, dagStats } = useDagInteractions()
  const reactFlowInstance = useRef<ReturnType<typeof import('@xyflow/react').useReactFlow> | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [summaryOpen, setSummaryOpen] = useState(false)

  const selectedNodeForDialog = useMemo(() => {
    if (!selectedNodeId || !dag?.nodes) return null
    return dag.nodes.find(n => n.id === selectedNodeId) || null
  }, [dag, selectedNodeId])

  // Track if a refresh is in progress to prevent request piling
  const refreshInProgress = useRef(false)

  // Silent refresh DAG from API (no loading indicator)
  // Uses a guard to prevent concurrent requests from piling up
  const silentRefresh = useCallback(async () => {
    if (!sessionId || !isConnected) return
    if (refreshInProgress.current) {
      console.log('[DAG] Skipping refresh - previous request still in progress')
      return
    }

    refreshInProgress.current = true
    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), 8000) // 8s timeout

    try {
      const snapshot = await getDagSnapshot(sessionId)
      if (snapshot) {
        const nodes: DAGNode[] = (snapshot.nodes || []).map(n => ({
          id: n.id,
          label: n.label || n.id,
          status: 'completed',
          type: n.type || 'node',
          kind: n.kind as NodeKind | undefined,
          owner: n.owner,
          proof: n.proof,
          attestations: n.attestations,
          attestationsCount: n.attestationsCount,
          sessionId: n.sessionId,  // Include sessionId for cross-session rendering
          // Map plan_status from API to planStatus for Plan nodes
          planStatus: n.planStatus as 'Pending' | 'Active' | 'Completed' | undefined,
        }))
        const edges = (snapshot.edges || []).map(e => ({
          source: e.fromId,
          target: e.toId,
          type: e.type,
        }))
        setDag({ nodes, edges })
      }
    } catch (e) {
      if ((e as Error)?.name !== 'AbortError') {
        console.error("Failed to refresh DAG:", e)
      }
    } finally {
      clearTimeout(timeoutId)
      refreshInProgress.current = false
    }
  }, [sessionId, isConnected, setDag])

  // Manual refresh with loading indicator
  const handleRefresh = useCallback(async () => {
    setRefreshing(true)
    await silentRefresh()
    setRefreshing(false)
  }, [silentRefresh])

  // Auto-refresh DAG every 5 seconds when connected
  // Increased from 3s to reduce request load and prevent connection piling
  useEffect(() => {
    if (!sessionId || !isConnected) return

    // Initial fetch
    silentRefresh()

    // Set up polling interval (5 seconds)
    const intervalId = setInterval(silentRefresh, 5000)

    return () => clearInterval(intervalId)
  }, [sessionId, isConnected, silentRefresh])

  // Convert graph data to ReactFlow format
  const { nodes: initialNodes, edges: initialEdges } = useMemo(() => {
    if (!dag?.nodes || dag.nodes.length === 0) {
      return { nodes: [] as Node[], edges: [] as Edge[] }
    }

    const isHighlighting = highlightMode !== 'none'

    // Debug: log first node's sessionId to verify data flow
    if (dag.nodes.length > 0) {
      console.log('[DAG] First node sessionId:', dag.nodes[0].sessionId, 'current sessionId:', sessionId)
    }

    // Debug: log activeMilestoneNodeId matching
    if (activeMilestoneNodeId) {
      const matchingNode = dag.nodes.find(n => n.id === activeMilestoneNodeId)
      console.log('[DAG] activeMilestoneNodeId:', activeMilestoneNodeId, 'matching node:', matchingNode?.id || 'NOT FOUND')
      if (!matchingNode) {
        console.log('[DAG] Available Plan node IDs:', dag.nodes.filter(n => n.kind === 'Plan').map(n => n.id))
      }
    }

    // First pass: determine which nodes will be rendered (limit to 200 for performance)
    // Sort nodes to prioritize current session, then Plan nodes, then by kind
    const sortedNodes = [...dag.nodes].sort((a, b) => {
      // 1. Current session nodes first
      const aCurrentSession = a.sessionId === sessionId ? 0 : 1
      const bCurrentSession = b.sessionId === sessionId ? 0 : 1
      if (aCurrentSession !== bCurrentSession) return aCurrentSession - bCurrentSession

      // 2. Plan nodes before Knowledge nodes (within same session priority)
      const aKind = a.kind === 'Plan' ? 0 : 1
      const bKind = b.kind === 'Plan' ? 0 : 1
      if (aKind !== bKind) return aKind - bKind

      // 3. Keep original order for nodes with same priority
      return 0
    })
    const renderedDagNodes = sortedNodes.slice(0, 500)
    const visibleNodeIds = new Set(renderedDagNodes.map(n => n.id))

    const nodes: Node[] = renderedDagNodes.map((node) => {
      const isSelected = node.id === selectedNodeId
      const isHighlighted = highlightedNodeIds.includes(node.id)
      const isDimmed = isHighlighting && !isSelected && !isHighlighted && node.id !== selectedNodeId
      // Check if node belongs to a different session
      const isOtherSession = node.sessionId ? node.sessionId !== sessionId : false
      // Determine plan status: Active if this is the currently executing milestone
      const isActiveMilestone = node.id === activeMilestoneNodeId
      const planStatus = isActiveMilestone
        ? 'Active'
        : (node.planStatus as 'Pending' | 'Active' | 'Completed' | undefined)

      return {
        id: node.id,
        type: 'cyber',
        data: {
          id: node.id,
          label: node.label || node.id,
          status: node.status || 'pending',
          selected: isSelected,
          kind: node.kind as 'Plan' | 'Knowledge' | undefined,
          planStatus,
          highlighted: isHighlighted,
          dimmed: isDimmed,
          isOtherSession,
        },
        position: { x: 0, y: 0 },
      }
    })

    // Filter edges to only include those where BOTH source and target are visible
    // This ensures edges are properly rendered (React Flow can't render edges to non-existent nodes)
    const edges: Edge[] = (dag.edges || [])
      .filter((edge) => visibleNodeIds.has(edge.source) && visibleNodeIds.has(edge.target))
      .slice(0, 1000)
      .map((edge, i) => {
        // Different styles for different edge types
        const isMotivatedBy = edge.type === 'motivated_by'
        const edgeColor = isMotivatedBy ? '#f59e0b' : '#00f0ff'  // Orange for motivated_by, Cyan for depends_on

        return {
          id: `e-${edge.source}-${edge.target}-${i}`,
          source: edge.source,
          target: edge.target,
          animated: true,
          style: {
            stroke: edgeColor,
            strokeWidth: isMotivatedBy ? 1.5 : 2,
            strokeDasharray: isMotivatedBy ? '5 3' : undefined,  // Dashed line for motivated_by
          },
          markerEnd: {
            type: MarkerType.ArrowClosed,
            color: edgeColor,
            width: isMotivatedBy ? 16 : 20,
            height: isMotivatedBy ? 16 : 20,
          },
          label: isMotivatedBy ? '✨' : undefined,  // Small indicator for motivated_by
          labelStyle: isMotivatedBy ? { fontSize: 10 } : undefined,
        }
      })

    return getLayoutedElements(nodes, edges, 'TB')
  }, [dag, selectedNodeId, highlightMode, highlightedNodeIds, sessionId, activeMilestoneNodeId])

  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges)

  // Sync nodes/edges when graph data changes
  useEffect(() => {
    setNodes(initialNodes)
    setEdges(initialEdges)
    
    if (initialNodes.length > 0) {
      setTimeout(() => {
        reactFlowInstance.current?.fitView({ padding: 0.3, duration: 300 })
      }, 100)
    }
  }, [initialNodes, initialEdges, setNodes, setEdges])

  // Re-layout handler
  const onLayout = useCallback((direction: 'TB' | 'LR') => {
    const { nodes: layoutedNodes, edges: layoutedEdges } = getLayoutedElements(
      nodes,
      edges,
      direction
    )
    setNodes([...layoutedNodes])
    setEdges([...layoutedEdges])
  }, [nodes, edges, setNodes, setEdges])

  // Handle node click
  const onNodeClick = useCallback((_: unknown, node: Node) => {
    setSelectedNode(node.id)
    clearHighlight() // Clear any existing highlights when selecting a new node
    setDetailsOpen(true)
  }, [setSelectedNode, clearHighlight])

  const nodeCount = dag?.nodes?.length ?? 0
  const edgeCount = dag?.edges?.length ?? 0

  // Empty state
  if (!dag?.nodes || dag.nodes.length === 0) {
    return (
      <div className={cn("card flex flex-col", fullHeight && "h-full")}>
        <TopologyHeader
          onLayout={onLayout}
          onRefresh={handleRefresh}
          refreshing={refreshing}
          nodeCount={0}
          edgeCount={0}
          activeMilestone={activeMilestoneNodeId}
        />
        <div className="flex-1 flex flex-col items-center justify-center py-8 text-center min-h-[300px]">
          <div className="relative">
            <div className="absolute inset-0 rounded-full bg-accent-emerald blur-2xl opacity-20 animate-pulse" />
            <div className="relative flex h-20 w-20 items-center justify-center rounded-full border-2 border-border bg-surface">
              <GitBranch className="h-10 w-10 text-text-muted" />
            </div>
          </div>
          <p className="mt-6 text-base text-text-muted font-mono">
            AWAITING TOPOLOGY DATA
          </p>
          <p className="mt-2 text-xs text-text-dimmed font-mono max-w-xs">
            Graph will materialize as reasoning progresses
          </p>
          {isConnected && (
            <button
              onClick={handleRefresh}
              disabled={refreshing}
              className="mt-4 px-4 py-2 text-xs font-mono rounded-lg border border-neon-cyan/40 text-neon-cyan hover:bg-neon-cyan/10 transition-colors disabled:opacity-50"
            >
              {refreshing ? "Loading..." : "Load DAG"}
            </button>
          )}
        </div>
      </div>
    )
  }

  return (
    <div className={cn("card flex flex-col overflow-hidden", fullHeight && "h-full")}>
      <TopologyHeader
        onLayout={onLayout}
        onRefresh={handleRefresh}
        onCollapse={onCollapse}
        onSummary={() => setSummaryOpen(true)}
        refreshing={refreshing}
        nodeCount={nodeCount}
        edgeCount={edgeCount}
        planCount={dagStats.planCount}
        knowledgeCount={dagStats.knowledgeCount}
        activeMilestone={activeMilestoneNodeId}
      />

      <div className="flex-1 flex min-h-[350px]">
        {/* ReactFlow Canvas */}
        <div className="flex-1 relative">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
            onNodeClick={onNodeClick}
          nodeTypes={nodeTypes}
          fitView
          fitViewOptions={{ padding: 0.3, maxZoom: 1.5, minZoom: 0.1 }}
          defaultViewport={{ x: 0, y: 0, zoom: 0.5 }}
          minZoom={0.1}
          maxZoom={2}
          onInit={(instance) => { 
            reactFlowInstance.current = instance
            instance.fitView({ padding: 0.3 })
          }}
          proOptions={{ hideAttribution: true }}
          style={{ background: 'transparent' }}
        >
          <Background
            variant={BackgroundVariant.Dots}
            gap={20}
            size={1}
            color="rgba(0, 255, 136, 0.15)"
          />
          <Controls
            className="!bg-bg-surface/90 !backdrop-blur-md !border !border-neon-cyan/30 !rounded-lg !shadow-none [&>button]:!bg-transparent [&>button]:!border-0 [&>button]:!border-b [&>button]:!border-border-subtle [&>button]:!text-neon-cyan [&>button]:!w-8 [&>button]:!h-8 [&>button]:!p-0 [&>button:hover]:!bg-neon-cyan/20 [&>button:last-child]:!border-b-0 [&>button>svg]:!w-4 [&>button>svg]:!h-4 [&>button>svg]:!fill-neon-cyan"
            position="bottom-right"
          />
        </ReactFlow>
        </div>
      </div>

      {/* Node details modal (on node click) - Split layout with SubGraph + Markdown */}
      <Dialog
        open={detailsOpen}
        onOpenChange={(open) => {
          setDetailsOpen(open)
          if (!open) {
            setSelectedNode(null)
            clearHighlight()
          }
        }}
      >
        <DialogContent className="bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-xl shadow-lg text-text-primary overflow-hidden max-w-7xl w-[90vw]">
          <DialogHeader>
            <div className="min-w-0">
              <DialogTitle>Node details</DialogTitle>
              <DialogDescription>
                {selectedNodeForDialog?.id ? (
                  <span className="font-mono tabular-nums">{selectedNodeForDialog.id}</span>
                ) : (
                  <span className="font-mono">—</span>
                )}
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
                  onNodeSelect={(nodeId) => {
                    setSelectedNode(nodeId)
                    // The useEffect in NodeDetailsPanel will trigger API call
                  }}
                />
              )}
            </div>

            {/* Right Panel - Markdown & Details */}
            <div className="flex-[1.5] min-w-[350px] overflow-y-auto">
              <div className="p-4">
                <NodeDetailsPanel sessionId={sessionId} />
              </div>
            </div>
          </div>
        </DialogContent>
      </Dialog>

      {/* Summary Modal (US7) */}
      <SummaryModal
        open={summaryOpen}
        onOpenChange={setSummaryOpen}
        sessionId={sessionId}
      />
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// Header Component
// ─────────────────────────────────────────────────────────────

interface TopologyHeaderProps {
  onLayout: (direction: 'TB' | 'LR') => void
  onRefresh: () => void
  onCollapse?: () => void
  onSummary?: () => void
  refreshing: boolean
  nodeCount: number
  edgeCount: number
  planCount?: number
  knowledgeCount?: number
  activeMilestone?: string | null
}

function TopologyHeader({ onLayout, onRefresh, onCollapse, onSummary, refreshing, nodeCount, edgeCount, planCount = 0, knowledgeCount = 0, activeMilestone }: TopologyHeaderProps) {
  return (
    <div className="flex-shrink-0 flex items-center justify-between p-4 border-b border-accent-emerald/30 bg-gradient-to-r from-accent-emerald/10 to-transparent">
      <div className="flex items-center gap-3">
        <div className="relative">
          <div className="absolute inset-0 rounded-xl bg-accent-emerald blur-lg opacity-40 animate-pulse" />
          <div className="relative flex h-11 w-11 items-center justify-center rounded-xl bg-gradient-to-br from-accent-emerald/20 to-accent-emerald/5 border-2 border-accent-emerald/50">
            <Network className="h-5 w-5 text-accent-emerald" />
          </div>
        </div>
        <div>
          <h3 className="font-mono text-sm font-bold tracking-wider text-accent-emerald text-balance">
            TOPOLOGY MAP
          </h3>
          <p className="text-[10px] text-text-muted font-mono tracking-wide text-pretty">
            {nodeCount > 0 ? (
              <span>
                n={nodeCount} e={edgeCount}
                {(planCount > 0 || knowledgeCount > 0) && (
                  <span className="ml-2">
                    (<span className="text-blue-400">P:{planCount}</span>{' '}
                    <span className="text-green-400">K:{knowledgeCount}</span>)
                  </span>
                )}
              </span>
            ) : 'Workflow Dependency Graph'}
          </p>
          {/* Debug: Show active milestone */}
          {activeMilestone && (
            <p className="text-[9px] text-orange-400 font-mono truncate max-w-[200px]">
              🔥 Active: {activeMilestone}
            </p>
          )}
        </div>
      </div>

      <div className="flex items-center gap-2">
        {/* Summary Button */}
        {onSummary && nodeCount > 0 && (
          <button
            onClick={onSummary}
            aria-label="Generate Summary"
            className="p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-green hover:border-neon-green/40 transition-all"
            title="Generate Summary"
          >
            <FileText className="size-3.5" />
          </button>
        )}

        {/* Collapse */}
        {onCollapse && (
          <button
            onClick={onCollapse}
            aria-label="Collapse DAG panel"
            className="p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-gold hover:border-neon-gold/40 transition-all"
          >
            <svg className="size-3.5" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
            </svg>
          </button>
        )}

        {/* Refresh */}
        <button
          onClick={onRefresh}
          disabled={refreshing}
          aria-label="Refresh DAG"
          className={cn(
            "p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/40 transition-all disabled:opacity-50",
            refreshing && "animate-spin"
          )}
        >
          <RefreshCw className="size-3.5" />
        </button>

        {/* Layout Buttons - Prominent */}
        <div className="flex items-center gap-1 p-1 rounded-lg bg-bg-elevated border border-border-subtle">
          <button
            onClick={() => onLayout('TB')}
            aria-label="Layout graph vertically"
            className="group px-3 py-2 text-xs font-mono font-semibold tracking-wide rounded-md bg-neon-cyan/10 border border-neon-cyan/40 text-neon-cyan hover:bg-neon-cyan/20 hover:border-neon-cyan/60 active:scale-95 transition-all duration-200 cursor-pointer flex items-center gap-2 shadow-[0_0_12px_rgba(0,240,255,0.15)]"
          >
            <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m0 0l-4-4m4 4l4-4" />
            </svg>
            <span>Vertical</span>
          </button>
          <button
            onClick={() => onLayout('LR')}
            aria-label="Layout graph horizontally"
            className="group px-3 py-2 text-xs font-mono font-semibold tracking-wide rounded-md bg-neon-gold/10 border border-neon-gold/40 text-neon-gold hover:bg-neon-gold/20 hover:border-neon-gold/60 active:scale-95 transition-all duration-200 cursor-pointer flex items-center gap-2 shadow-[0_0_12px_rgba(255,215,0,0.15)]"
          >
            <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 12h16m0 0l-4-4m4 4l-4 4" />
            </svg>
            <span>Horizontal</span>
          </button>
        </div>
      </div>
    </div>
  )
}

export default WorkflowTopology
