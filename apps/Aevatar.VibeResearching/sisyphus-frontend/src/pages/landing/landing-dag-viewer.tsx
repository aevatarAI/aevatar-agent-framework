// ============================================================
//  Landing DAG Viewer - Interactive Full DAG Display
//  Data source: Global DAG API (real data from backend)
// ============================================================

import { useCallback, useMemo, useState, useRef, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
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
import { RotateCcw, Sparkles, Loader2, GitBranch, RefreshCw } from 'lucide-react'
import { cn } from '@/lib/utils'
import { nodeTypes } from '@/components/sisyphus/workflow-topology/cyber-node'
import { getLayoutedElements } from '@/components/sisyphus/workflow-topology/dag-layout'
import { getGlobalDagSnapshot, type DagSnapshot, type DagNode as ApiDagNode } from '@/lib/axiom-client'
import type { DAGGraph, DAGNode } from '@/types'
import '@xyflow/react/dist/style.css'

// ─── Transform API data to frontend format ───
function transformApiData(snapshot: DagSnapshot): DAGGraph {
  const nodes: DAGNode[] = (snapshot.nodes || []).map((node: ApiDagNode) => ({
    id: node.id,
    label: node.label || node.id,
    kind: (node.kind as 'Plan' | 'Knowledge') || 'Knowledge',
    status: 'completed',
    type: node.type || 'Knowledge',
    proof: node.proof,
    attestationsCount: node.attestationsCount,
    planStatus: node.planStatus as 'Pending' | 'Active' | 'Completed' | undefined,
    sessionId: node.sessionId,
  }))

  const edges = (snapshot.edges || []).map((edge) => ({
    source: edge.fromId,
    target: edge.toId,
    type: edge.type,
  }))

  return { nodes, edges }
}

// ─── Calculate Stats ───
function calculateStats(data: DAGGraph | null) {
  if (!data) return { totalNodes: 0, totalEdges: 0, planCount: 0, knowledgeCount: 0 }
  
  const planNodes = data.nodes.filter(n => n.kind === 'Plan')
  const knowledgeNodes = data.nodes.filter(n => n.kind === 'Knowledge')
  
  return {
    totalNodes: data.nodes.length,
    totalEdges: data.edges.length,
    planCount: planNodes.length,
    knowledgeCount: knowledgeNodes.length,
  }
}

export function LandingDagViewer() {
  const navigate = useNavigate()
  const reactFlowInstance = useRef<ReactFlowInstance | null>(null)
  const [selectedNode, setSelectedNode] = useState<DAGNode | null>(null)

  // ─── Data Loading State ───
  const [dagData, setDagData] = useState<DAGGraph | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // ─── Fetch Global DAG Data ───
  const fetchDagData = useCallback(async () => {
    setIsLoading(true)
    setError(null)
    try {
      const snapshot = await getGlobalDagSnapshot()
      if (snapshot && snapshot.nodes && snapshot.nodes.length > 0) {
        const transformed = transformApiData(snapshot)
        setDagData(transformed)
      } else {
        setDagData(null)
      }
    } catch (err) {
      console.error('[LandingDagViewer] Failed to fetch DAG:', err)
      setError((err as Error)?.message || 'Failed to load knowledge graph')
      setDagData(null)
    } finally {
      setIsLoading(false)
    }
  }, [])

  // Fetch on mount
  useEffect(() => {
    fetchDagData()
  }, [fetchDagData])

  const stats = useMemo(() => calculateStats(dagData), [dagData])

  // ─── Transform DAG to ReactFlow Format ───
  const { nodes: initialNodes, edges: initialEdges } = useMemo(() => {
    if (!dagData) return { nodes: [], edges: [] }

    // Show all nodes (no filtering in Landing Page)
    const visibleNodeIds = new Set(dagData.nodes.map(n => n.id))

    const nodes: Node[] = dagData.nodes.map((node) => {
      const isSelected = node.id === selectedNode?.id
      const isActivePlan = node.kind === 'Plan' && node.planStatus === 'Active'

      return {
        id: node.id,
        type: 'cyber',
        data: {
          id: node.id,
          label: node.label,
          status: node.status || 'pending',
          selected: isSelected,
          kind: node.kind as 'Plan' | 'Knowledge' | undefined,
          planStatus: node.planStatus as 'Pending' | 'Active' | 'Completed' | undefined,
          highlighted: isActivePlan,
          dimmed: false,
          isOtherSession: false,
        },
        position: { x: 0, y: 0 },
      }
    })

    const filteredEdges = dagData.edges.filter(
      (edge) => visibleNodeIds.has(edge.source) && visibleNodeIds.has(edge.target)
    )

    const edges: Edge[] = filteredEdges.map((edge, i) => {
      const isMotivatedBy = edge.type === 'motivated_by'
      const edgeColor = isMotivatedBy ? '#f59e0b' : '#00f0ff'

      return {
        id: `e-${edge.source}-${edge.target}-${i}`,
        source: edge.source,
        target: edge.target,
        animated: true,
        style: {
          stroke: edgeColor,
          strokeWidth: isMotivatedBy ? 1.5 : 2,
          strokeDasharray: isMotivatedBy ? '5 3' : undefined,
        },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: edgeColor,
          width: isMotivatedBy ? 16 : 20,
          height: isMotivatedBy ? 16 : 20,
        },
        label: isMotivatedBy ? '✨' : undefined,
        labelStyle: isMotivatedBy ? { fontSize: 10 } : undefined,
      }
    })

    return getLayoutedElements(nodes, edges, 'TB')
  }, [dagData, selectedNode])

  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges)

  // Update nodes/edges when data or filter changes
  useEffect(() => {
    setNodes(initialNodes)
    setEdges(initialEdges)
  }, [initialNodes, initialEdges, setNodes, setEdges])

  // ─── Fit View after nodes update ───
  useEffect(() => {
    if (reactFlowInstance.current && nodes.length > 0) {
      setTimeout(() => {
        reactFlowInstance.current?.fitView({ padding: 0.15, duration: 400 })
      }, 100)
    }
  }, [nodes.length])

  // ─── Node Click Handler ───
  const onNodeClick = useCallback((_: unknown, node: Node) => {
    if (!dagData) return
    const dagNode = dagData.nodes.find(n => n.id === node.id)
    setSelectedNode(dagNode || null)
  }, [dagData])

  // ─── Reset View ───
  const handleResetView = useCallback(() => {
    reactFlowInstance.current?.fitView({ padding: 0.15, duration: 400 })
  }, [])

  // ─── Empty State Component ───
  const EmptyState = () => (
    <div className="flex-1 flex items-center justify-center p-8">
      <div className="text-center max-w-md">
        {/* Decorative Icon */}
        <div className="relative mx-auto mb-8">
          <div className="size-24 rounded-2xl bg-gradient-to-br from-neon-cyan/20 to-neon-purple/20 flex items-center justify-center border border-neon-cyan/30">
            <GitBranch className="size-12 text-neon-cyan/60" />
          </div>
          {/* Floating particles */}
          <div className="absolute -top-2 -right-2 size-4 rounded-full bg-neon-gold/40 animate-pulse" />
          <div className="absolute -bottom-1 -left-1 size-3 rounded-full bg-neon-purple/40 animate-pulse" style={{ animationDelay: '0.5s' }} />
        </div>

        <h3 className="font-display text-xl font-bold text-text-primary mb-3">
          Knowledge Graph Awaits
        </h3>
        
        <p className="text-sm text-text-secondary leading-relaxed mb-6">
          No research sessions yet. Start your first research session to build your knowledge graph — every hypothesis verified, every fact connected.
        </p>

        <div className="flex flex-col items-center gap-3">
          <button
            onClick={() => navigate('/app')}
            className="inline-flex items-center gap-2 px-6 py-3 rounded-xl bg-neon-cyan text-bg-base font-semibold transition-all hover:shadow-glow-cyan hover:-translate-y-0.5"
          >
            <Sparkles className="size-4" />
            <span>Start First Research</span>
          </button>
          
          <button
            onClick={fetchDagData}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm text-text-muted hover:text-neon-cyan transition-colors"
          >
            <RefreshCw className="size-3" />
            <span>Refresh</span>
          </button>
        </div>

        {/* Bottom hint */}
        <p className="mt-8 text-xs text-text-dimmed font-mono">
          "Facts are nodes. Derivations are edges."
        </p>
      </div>
    </div>
  )

  // ─── Loading State Component ───
  const LoadingState = () => (
    <div className="flex-1 flex items-center justify-center">
      <div className="text-center">
        <Loader2 className="size-12 text-neon-cyan animate-spin mx-auto mb-4" />
        <p className="text-sm text-text-muted font-mono">Loading Knowledge Graph...</p>
      </div>
    </div>
  )

  // ─── Error State Component ───
  const ErrorState = () => (
    <div className="flex-1 flex items-center justify-center p-8">
      <div className="text-center max-w-md">
        <div className="size-16 rounded-xl bg-red-500/20 flex items-center justify-center mx-auto mb-4 border border-red-500/30">
          <X className="size-8 text-red-400" />
        </div>
        <h3 className="font-display text-lg font-bold text-text-primary mb-2">
          Failed to Load
        </h3>
        <p className="text-sm text-text-secondary mb-4">{error}</p>
        <button
          onClick={fetchDagData}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-neon-cyan/50 text-neon-cyan hover:bg-neon-cyan/10 transition-all"
        >
          <RefreshCw className="size-4" />
          <span>Try Again</span>
        </button>
      </div>
    </div>
  )

  // ─── Determine content to render ───
  const renderContent = () => {
    if (isLoading) return <LoadingState />
    if (error) return <ErrorState />
    if (!dagData || dagData.nodes.length === 0) return <EmptyState />

    return (
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onNodeClick={onNodeClick}
        nodeTypes={nodeTypes}
        fitView
        fitViewOptions={{ padding: 0.15, maxZoom: 1.5, minZoom: 0.1 }}
        minZoom={0.1}
        maxZoom={2}
        onInit={(instance) => { reactFlowInstance.current = instance }}
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
    )
  }

  return (
    <div className="flex h-[600px]">
      {/* ─── Left: DAG Visualization ─── */}
      <div className="flex-[1.5] relative">
        {renderContent()}

        {/* ─── Top Controls (only show when data exists) ─── */}
        {dagData && dagData.nodes.length > 0 && (
          <div className="absolute top-4 right-4 z-30 flex items-center gap-2">
            <button
              onClick={fetchDagData}
              className="p-2 rounded-lg bg-bg-surface/90 backdrop-blur-md border border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/30 transition-all"
              title="Refresh Data"
            >
              <RefreshCw className="size-4" />
            </button>
            <button
              onClick={handleResetView}
              className="p-2 rounded-lg bg-bg-surface/90 backdrop-blur-md border border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/30 transition-all"
              title="Reset View"
            >
              <RotateCcw className="size-4" />
            </button>
          </div>
        )}
      </div>

      {/* ─── Right: Sidebar ─── */}
      <div className="w-80 border-l border-border-subtle bg-bg-surface/80 backdrop-blur-md overflow-y-auto flex flex-col">
        <div className="p-4 space-y-6 flex-1">
          {/* Stats - Global DAG Knowledge Count */}
          <div className="space-y-3">
            <h3 className="font-display text-xs text-neon-cyan tracking-wider">KNOWLEDGE GRAPH</h3>
            <StatCard label="KNOWLEDGE NODES" value={stats.knowledgeCount} color="green" />
          </div>

          {/* Selected Node */}
          {selectedNode && (
            <div className="space-y-3">
              <h3 className="font-display text-xs text-neon-purple tracking-wider">SELECTED NODE</h3>
              <div className="p-4 rounded-lg border border-border-subtle bg-bg-base/50 space-y-3">
                <div className="flex items-center gap-2">
                  <span className={cn(
                    'px-2 py-0.5 text-xs font-mono rounded border',
                    selectedNode.kind === 'Plan'
                      ? 'bg-neon-purple/20 text-neon-purple border-neon-purple/30'
                      : 'bg-neon-green/20 text-neon-green border-neon-green/30'
                  )}>
                    {selectedNode.kind || 'Node'}
                  </span>
                  {selectedNode.kind === 'Plan' && selectedNode.planStatus && (
                    <span className={cn(
                      'px-2 py-0.5 text-xs font-mono rounded border',
                      selectedNode.planStatus === 'Active'
                        ? 'bg-neon-gold/20 text-neon-gold border-neon-gold/30'
                        : selectedNode.planStatus === 'Completed'
                        ? 'bg-neon-green/20 text-neon-green border-neon-green/30'
                        : 'bg-bg-accent text-text-muted border-border-subtle'
                    )}>
                      {selectedNode.planStatus}
                    </span>
                  )}
                </div>
                <p className="text-sm font-medium text-text-primary leading-snug">
                  {selectedNode.label}
                </p>
                <p className="text-xs text-text-dimmed font-mono">
                  {selectedNode.id}
                </p>
                {selectedNode.proof && (
                  <div className="pt-2 border-t border-border-subtle">
                    <span className="text-xs text-neon-cyan">Proof:</span>
                    <p className="text-xs text-text-secondary mt-1">
                      {selectedNode.proof}
                    </p>
                  </div>
                )}
                {selectedNode.attestationsCount && (
                  <div className="flex items-center gap-2 text-xs text-text-muted">
                    <span className="text-neon-gold">✓</span>
                    <span>{selectedNode.attestationsCount} attestations</span>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        {/* CTA */}
        <div className="p-4 border-t border-border-subtle">
          <button
            onClick={() => navigate('/app')}
            className="w-full flex items-center justify-center gap-2 px-4 py-3 rounded-xl bg-neon-cyan text-bg-base font-semibold transition-all hover:shadow-glow-cyan hover:-translate-y-0.5"
          >
            <Sparkles className="size-4" />
            <span>Start Your Research</span>
          </button>
          <p className="mt-2 text-xs text-text-dimmed text-center">
            Build your own knowledge graph
          </p>
        </div>
      </div>
    </div>
  )
}

// ─── Stat Card Component ───
function StatCard({ label, value, color, className }: { label: string; value: number; color: 'cyan' | 'gold' | 'purple' | 'green'; className?: string }) {
  const colorClasses = {
    cyan: 'border-neon-cyan/30 text-neon-cyan',
    gold: 'border-neon-gold/30 text-neon-gold',
    purple: 'border-neon-purple/30 text-neon-purple',
    green: 'border-neon-green/30 text-neon-green',
  }

  return (
    <div className={cn('p-3 rounded-lg border bg-bg-base/50', colorClasses[color], className)}>
      <div className="font-display text-2xl font-bold">{value}</div>
      <div className="text-[10px] font-mono text-text-dimmed tracking-wider">{label}</div>
    </div>
  )
}
