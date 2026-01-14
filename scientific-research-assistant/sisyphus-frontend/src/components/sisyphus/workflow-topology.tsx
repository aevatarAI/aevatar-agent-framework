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
import { Network, GitBranch, RefreshCw, ChevronDown, ChevronRight } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useSisyphusStore } from '@/store/sisyphus-store'
import type { DAGNode } from '@/types'
import { getDagSnapshot, getDagNodeExplain, getKnowledgeChain } from '@/lib/axiom-client'
import { cn } from '@/lib/utils'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog'
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
// Node Style (Unified)
// ─────────────────────────────────────────────────────────────

const NODE_STYLE = { 
  bg: '#00f0ff', 
  border: '#33f4ff', 
  glow: 'rgba(0, 240, 255, 0.6)' 
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
}

function CyberNode({ data }: { data: CyberNodeData }) {
  const [showTooltip, setShowTooltip] = useState(false)
  const opacity = STATUS_OPACITY[data.status] || 1

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
          data.selected && "ring-2 ring-neon-gold ring-offset-2 ring-offset-bg-base"
        )}
        style={{
          width: 48,
          height: 48,
          background: `radial-gradient(circle, ${NODE_STYLE.bg} 0%, ${NODE_STYLE.border} 100%)`,
          border: `2px solid ${data.selected ? '#ffd700' : NODE_STYLE.border}`,
          boxShadow: data.selected 
            ? `0 0 24px rgba(255,215,0,0.5), inset 0 0 10px rgba(255,255,255,0.2)`
            : `0 0 20px ${NODE_STYLE.glow}, inset 0 0 10px rgba(255,255,255,0.2)`,
          opacity,
        }}
        onMouseEnter={() => setShowTooltip(true)}
        onMouseLeave={() => setShowTooltip(false)}
      >
        <span
          className="font-mono font-extrabold text-[10px] tracking-wider"
          style={{ 
            color: '#0a0f19',
            textShadow: `0 0 2px ${NODE_STYLE.bg}` 
          }}
        >
          {data.id.slice(0, 4)}
        </span>
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
              border: `2px solid ${NODE_STYLE.border}`,
              boxShadow: `0 0 30px ${NODE_STYLE.glow}, 0 4px 20px rgba(0,0,0,0.5)`,
            }}
          >
            <div 
              className="font-bold mb-2 pb-2 border-b border-slate-600/50"
              style={{ color: NODE_STYLE.bg }}
            >
              {data.id}
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
              borderTop: `8px solid ${NODE_STYLE.border}`,
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
// Node Details Panel
// ─────────────────────────────────────────────────────────────

interface NodeDetailsPanelProps {
  sessionId: string
}

function NodeDetailsPanel({ sessionId }: NodeDetailsPanelProps) {
  const { 
    dag, 
    selectedNodeId, 
    dagNodeExplain, 
    dagKnowledgeChain, 
    dagLoading, 
    dagError,
    setDagNodeExplain,
    setDagKnowledgeChain,
    setDagLoading,
    setDagError,
  } = useSisyphusStore()

  const selectedNode = useMemo(() => {
    if (!selectedNodeId || !dag?.nodes) return null
    return dag.nodes.find(n => n.id === selectedNodeId)
  }, [dag, selectedNodeId])

  // Load explain when node selected
  useEffect(() => {
    if (!selectedNodeId || !sessionId) return
    
    let cancelled = false
    setDagLoading(true)
    setDagError("")
    
    Promise.all([
      getDagNodeExplain(sessionId, selectedNodeId),
      getKnowledgeChain(sessionId, selectedNodeId),
    ]).then(([explain, chain]) => {
      if (cancelled) return
      setDagNodeExplain(explain)
      setDagKnowledgeChain(chain)
    }).catch((e) => {
      if (cancelled) return
      setDagError(e?.message || "Failed to load node details")
    }).finally(() => {
      if (!cancelled) setDagLoading(false)
    })

    return () => { cancelled = true }
  }, [selectedNodeId, sessionId, setDagNodeExplain, setDagKnowledgeChain, setDagLoading, setDagError])

  if (!selectedNode) {
    return (
      <div className="text-xs text-text-muted text-center py-4">
        Select a node to view details
      </div>
    )
  }

  const shortKey = (s: string, head = 10, tail = 8) => {
    const t = (s || "").trim()
    if (!t || t.length <= head + tail + 3) return t
    return `${t.slice(0, head)}…${t.slice(-tail)}`
  }

  return (
    <div className="space-y-3">
      {/* Node ID & Type */}
      <div>
        <div className="text-xs font-mono text-neon-cyan break-all">{selectedNode.id}</div>
        <div className="mt-1 flex items-center gap-2 flex-wrap">
          {selectedNode.type && (
            <span className="text-[10px] px-1.5 py-0.5 rounded bg-surface-elevated border border-border-subtle text-text-muted">
              {selectedNode.type}
            </span>
          )}
          {selectedNode.kind && (
            <span className="text-[10px] px-1.5 py-0.5 rounded bg-surface-elevated border border-border-subtle text-text-muted">
              kind:{selectedNode.kind}
            </span>
          )}
          {dagNodeExplain && (
            <span className={cn(
              "text-[10px] px-1.5 py-0.5 rounded border",
              dagNodeExplain.hasCycle 
                ? "bg-neon-rose/20 border-neon-rose/30 text-neon-rose"
                : dagNodeExplain.provable 
                  ? "bg-neon-green/20 border-neon-green/30 text-neon-green"
                  : "bg-neon-gold/20 border-neon-gold/30 text-neon-gold"
            )}>
              {dagNodeExplain.hasCycle ? "cycle" : dagNodeExplain.provable ? "provable" : "incomplete"}
            </span>
          )}
        </div>
      </div>

      {/* Owner */}
      {selectedNode.owner && (
        <div className="text-[11px]">
          <span className="text-text-muted">owner:</span>{" "}
          <span className="font-mono text-text-secondary">{shortKey(selectedNode.owner)}</span>
        </div>
      )}

      {/* Label */}
      {selectedNode.label && (
        <div className="text-xs text-text-secondary leading-relaxed">{selectedNode.label}</div>
      )}

      {dagLoading && (
        <div className="text-[11px] text-text-muted animate-pulse">Loading details...</div>
      )}

      {dagError && (
        <div className="text-[11px] text-neon-rose">{dagError}</div>
      )}

      {/* Explain Details */}
      {dagNodeExplain && (
        <DetailsSection title="Explain">
          <div className="space-y-1 text-[11px]">
            <div>
              <span className="text-text-muted">directDeps:</span>{" "}
              <span className="text-text-secondary">{dagNodeExplain.directDeps?.length ?? 0}</span>
            </div>
            <div>
              <span className="text-text-muted">missing:</span>{" "}
              <span className="text-text-secondary">{dagNodeExplain.missing?.length ?? 0}</span>
            </div>
            {dagNodeExplain.missing && dagNodeExplain.missing.length > 0 && (
              <div className="mt-2 space-y-1">
                {dagNodeExplain.missing.slice(0, 8).map((m, idx) => (
                  <div key={`${m.id}-${idx}`} className="text-neon-rose break-all">
                    - {m.id} {m.type ? `(${m.type})` : ""}
                  </div>
                ))}
                {dagNodeExplain.missing.length > 8 && (
                  <div className="text-text-dimmed">...and {dagNodeExplain.missing.length - 8} more</div>
                )}
              </div>
            )}
          </div>
        </DetailsSection>
      )}

      {/* Attestations */}
      {selectedNode.attestations && selectedNode.attestations.length > 0 && (
        <DetailsSection title={`Attestations (${selectedNode.attestations.length})`}>
          <div className="space-y-2">
            {selectedNode.attestations.slice(0, 5).map((a, idx) => (
              <div key={`${a.pubkey || ''}-${idx}`} className="text-[10px] font-mono">
                <div className="text-text-secondary break-all">pk: {shortKey(a.pubkey || '')}</div>
                <div className="text-text-dimmed break-all">sig: {shortKey(a.signature || '')}</div>
              </div>
            ))}
          </div>
        </DetailsSection>
      )}

      {/* Proof */}
      {selectedNode.proof && (
        <DetailsSection title="Proof">
          <pre className="text-[10px] text-text-secondary whitespace-pre-wrap break-words max-h-32 overflow-auto">
            {selectedNode.proof}
          </pre>
        </DetailsSection>
      )}

      {/* Knowledge Chain */}
      {dagKnowledgeChain && (
        <DetailsSection title="Knowledge Chain">
          <div className="prose prose-sm prose-invert max-w-none text-[11px] leading-relaxed
            prose-headings:text-neon-cyan prose-headings:text-xs
            prose-a:text-neon-cyan prose-code:text-neon-gold prose-code:text-[10px]
          ">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>{dagKnowledgeChain}</ReactMarkdown>
          </div>
        </DetailsSection>
      )}
    </div>
  )
}

// Collapsible Details Section
function DetailsSection({ title, children }: { title: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="rounded-lg border border-border-subtle bg-bg-elevated/50 overflow-hidden">
      <button
        onClick={() => setOpen(!open)}
        className="w-full px-3 py-2 flex items-center justify-between text-[11px] text-text-muted hover:text-text-secondary transition-colors"
      >
        <span>{title}</span>
        {open ? <ChevronDown className="size-3" /> : <ChevronRight className="size-3" />}
      </button>
      {open && (
        <div className="px-3 pb-3 pt-1 border-t border-border-subtle">
          {children}
        </div>
      )}
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────

function WorkflowTopology({ sessionId, fullHeight = false, onCollapse }: WorkflowTopologyProps) {
  const { dag, selectedNodeId, setDag, setSelectedNode, isConnected } = useSisyphusStore()
  const reactFlowInstance = useRef<ReturnType<typeof import('@xyflow/react').useReactFlow> | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [detailsOpen, setDetailsOpen] = useState(false)

  const selectedNodeForDialog = useMemo(() => {
    if (!selectedNodeId || !dag?.nodes) return null
    return dag.nodes.find(n => n.id === selectedNodeId) || null
  }, [dag, selectedNodeId])

  // Silent refresh DAG from API (no loading indicator)
  const silentRefresh = useCallback(async () => {
    if (!sessionId || !isConnected) return
    try {
      const snapshot = await getDagSnapshot(sessionId)
      if (snapshot) {
        const nodes: DAGNode[] = (snapshot.nodes || []).map(n => ({
          id: n.id,
          label: n.label || n.id,
          status: 'completed',
          type: n.type || 'node',
          kind: n.kind,
          owner: n.owner,
          proof: n.proof,
          attestations: n.attestations,
          attestationsCount: n.attestationsCount,
        }))
        const edges = (snapshot.edges || []).map(e => ({
          source: e.fromId,
          target: e.toId,
          type: e.type,
        }))
        setDag({ nodes, edges })
      }
    } catch (e) {
      console.error("Failed to refresh DAG:", e)
    }
  }, [sessionId, isConnected, setDag])

  // Manual refresh with loading indicator
  const handleRefresh = useCallback(async () => {
    setRefreshing(true)
    await silentRefresh()
    setRefreshing(false)
  }, [silentRefresh])

  // Auto-refresh DAG every 3 seconds when connected
  useEffect(() => {
    if (!sessionId || !isConnected) return
    
    // Initial fetch
    silentRefresh()
    
    // Set up polling interval
    const intervalId = setInterval(silentRefresh, 3000)
    
    return () => clearInterval(intervalId)
  }, [sessionId, isConnected, silentRefresh])

  // Convert graph data to ReactFlow format
  const { nodes: initialNodes, edges: initialEdges } = useMemo(() => {
    if (!dag?.nodes || dag.nodes.length === 0) {
      return { nodes: [] as Node[], edges: [] as Edge[] }
    }

    const nodes: Node[] = dag.nodes.slice(0, 200).map((node) => ({
      id: node.id,
      type: 'cyber',
      data: { 
        id: node.id,
        label: node.label || node.id,
        status: node.status || 'pending',
        selected: node.id === selectedNodeId,
      },
      position: { x: 0, y: 0 },
    }))

    const edges: Edge[] = (dag.edges || []).slice(0, 400).map((edge, i) => ({
      id: `e-${edge.source}-${edge.target}-${i}`,
      source: edge.source,
      target: edge.target,
      animated: true,
      style: { stroke: '#00f0ff', strokeWidth: 2 },
      markerEnd: {
        type: MarkerType.ArrowClosed,
        color: '#00f0ff',
        width: 20,
        height: 20,
      },
    }))

    return getLayoutedElements(nodes, edges, 'TB')
  }, [dag, selectedNodeId])

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
    setDetailsOpen(true)
  }, [setSelectedNode])

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
        refreshing={refreshing}
        nodeCount={nodeCount}
        edgeCount={edgeCount}
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

      {/* Node details modal (on node click) */}
      <Dialog
        open={detailsOpen}
        onOpenChange={(open) => {
          setDetailsOpen(open)
          if (!open) setSelectedNode(null)
        }}
      >
        <DialogContent className="bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-xl shadow-lg text-text-primary overflow-hidden">
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

          <div className="p-4 overflow-y-auto max-h-[60vh]">
            <NodeDetailsPanel sessionId={sessionId} />
          </div>
        </DialogContent>
      </Dialog>
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
  refreshing: boolean
  nodeCount: number
  edgeCount: number
}

function TopologyHeader({ onLayout, onRefresh, onCollapse, refreshing, nodeCount, edgeCount }: TopologyHeaderProps) {
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
            {nodeCount > 0 ? `n=${nodeCount} e=${edgeCount}` : 'Workflow Dependency Graph'}
          </p>
        </div>
      </div>
      
      <div className="flex items-center gap-2">
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
