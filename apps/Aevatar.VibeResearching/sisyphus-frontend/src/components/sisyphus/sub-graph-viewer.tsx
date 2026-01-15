import { useState, useMemo, useCallback, useRef, useEffect } from 'react'
import {
  ReactFlow,
  Background,
  BackgroundVariant,
  Handle,
  Position,
  type Node,
  type Edge,
  type ReactFlowInstance,
} from '@xyflow/react'
import dagre from 'dagre'
import { Minus, Plus, RotateCcw, ZoomIn, ZoomOut } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { DAGGraph, NodeKind } from '@/types'
import type { NodeExplanationData } from '@/store/sisyphus-store'
import { useDagInteractions } from '@/hooks/use-dag-interactions'

// ============================================================
//  SubGraphViewer - Mini DAG for Parent Chain Visualization
//  Used in node detail popup to show derivation/plan chain
// ============================================================

interface SubGraphViewerProps {
  dag: DAGGraph | null
  selectedNodeId: string
  selectedNodeKind: 'Plan' | 'Knowledge'
  /** Node explanation data (reserved for future use) */
  nodeExplanation?: NodeExplanationData | null
  onNodeSelect: (nodeId: string) => void
}

// Node colors matching main DAG
const NODE_COLORS = {
  Plan: {
    bg: '#3b82f6',
    border: '#60a5fa',
    glow: 'rgba(59, 130, 246, 0.8)',
  },
  Knowledge: {
    bg: '#22c55e',
    border: '#4ade80',
    glow: 'rgba(34, 197, 94, 0.8)',
  },
}

// ─────────────────────────────────────────────────────────────
// Mini Node Component for Sub-graph
// ─────────────────────────────────────────────────────────────

interface MiniNodeData extends Record<string, unknown> {
  id: string
  label: string
  kind: NodeKind
  isSelectedNode: boolean
  level: number
}

function MiniNode({ data }: { data: MiniNodeData }) {
  const colors = NODE_COLORS[data.kind] || NODE_COLORS.Knowledge
  const isSelected = data.isSelectedNode

  return (
    <>
      <Handle
        type="target"
        position={Position.Top}
        className="!w-1 !h-1 !bg-transparent !border-0"
      />
      <div
        className={cn(
          "flex items-center justify-center rounded-full cursor-pointer transition-all duration-200 hover:scale-110",
          isSelected && "animate-selected-glow"
        )}
        style={{
          width: 36,
          height: 36,
          background: `radial-gradient(circle, ${colors.bg} 0%, ${colors.border} 100%)`,
          border: `2px solid ${isSelected ? '#ffd700' : colors.border}`,
          boxShadow: isSelected
            ? `0 0 20px ${colors.glow}, 0 0 40px ${colors.glow}, 0 0 60px ${colors.glow}`
            : `0 0 12px ${colors.glow}`,
          // @ts-expect-error CSS custom property for animation
          '--glow-color': colors.glow,
        }}
        title={`${data.label}\n(${data.kind})`}
      >
        <span className="text-[9px]" style={{ color: '#0a0f19' }}>
          {data.kind === 'Plan' ? '📋' : '💡'}
        </span>
      </div>
      <Handle
        type="source"
        position={Position.Bottom}
        className="!w-1 !h-1 !bg-transparent !border-0"
      />
    </>
  )
}

const nodeTypes = { mini: MiniNode }

// ─────────────────────────────────────────────────────────────
// Dagre Layout Helper
// ─────────────────────────────────────────────────────────────

function applyDagreLayout(
  nodes: Node[],
  edges: Edge[],
  direction: 'TB' | 'BT' = 'BT' // Bottom-to-top for parent chain
): { nodes: Node[]; edges: Edge[] } {
  if (nodes.length === 0) return { nodes, edges }

  const dagreGraph = new dagre.graphlib.Graph()
  dagreGraph.setDefaultEdgeLabel(() => ({}))
  dagreGraph.setGraph({ rankdir: direction, nodesep: 40, ranksep: 50 })

  const nodeWidth = 36
  const nodeHeight = 36

  nodes.forEach((node) => {
    dagreGraph.setNode(node.id, { width: nodeWidth, height: nodeHeight })
  })

  edges.forEach((edge) => {
    dagreGraph.setEdge(edge.source, edge.target)
  })

  dagre.layout(dagreGraph)

  const layoutedNodes = nodes.map((node) => {
    const pos = dagreGraph.node(node.id)
    return {
      ...node,
      position: { x: pos.x - nodeWidth / 2, y: pos.y - nodeHeight / 2 },
      targetPosition: direction === 'TB' ? Position.Top : Position.Bottom,
      sourcePosition: direction === 'TB' ? Position.Bottom : Position.Top,
    }
  })

  return { nodes: layoutedNodes, edges }
}

// ─────────────────────────────────────────────────────────────
// SubGraphViewer Component
// ─────────────────────────────────────────────────────────────

export function SubGraphViewer({
  dag,
  selectedNodeId,
  selectedNodeKind,
  nodeExplanation: _nodeExplanation,
  onNodeSelect,
}: SubGraphViewerProps) {
  const [viewLevel, setViewLevel] = useState(1) // 0=current only, 1=+direct parents, 2=+grandparents...
  const { getUpstreamNodesByLevel } = useDagInteractions()
  const reactFlowInstance = useRef<ReactFlowInstance | null>(null)

  // Compute sub-graph nodes based on view level
  const { nodes, edges } = useMemo(() => {
    if (!dag || !selectedNodeId) return { nodes: [], edges: [] }

    // Get parent nodes by level (viewLevel - 1 because viewLevel 1 = direct parents = level 0)
    const parentsByLevel = getUpstreamNodesByLevel(selectedNodeId, dag, viewLevel > 0 ? viewLevel - 1 : -1)

    // Build visible node set - always include selected node
    const visibleNodeIds = new Set<string>([selectedNodeId])

    // Add parent nodes up to viewLevel - 1
    if (viewLevel > 0) {
      parentsByLevel.forEach((level, nodeId) => {
        if (level < viewLevel) {
          visibleNodeIds.add(nodeId)
        }
      })
    }

    // Find DAG nodes for visible IDs
    const visibleDagNodes = dag.nodes.filter((n) => visibleNodeIds.has(n.id))

    // Get kind for selected node from explanation or dag
    const getNodeKind = (nodeId: string): NodeKind => {
      if (nodeId === selectedNodeId) {
        return selectedNodeKind
      }
      const dagNode = dag.nodes.find((n) => n.id === nodeId)
      return (dagNode?.kind as NodeKind) || 'Knowledge'
    }

    // Convert to ReactFlow nodes
    const flowNodes: Node[] = visibleDagNodes.map((node) => ({
      id: node.id,
      type: 'mini',
      data: {
        id: node.id,
        label: node.label || node.id,
        kind: getNodeKind(node.id),
        isSelectedNode: node.id === selectedNodeId,
        level: parentsByLevel.get(node.id) ?? -1,
      } as MiniNodeData,
      position: { x: 0, y: 0 },
    }))

    // Filter edges to only include visible connections
    const flowEdges: Edge[] = dag.edges
      .filter((e) => visibleNodeIds.has(e.source) && visibleNodeIds.has(e.target))
      .map((e, i) => ({
        id: `sub-e-${i}`,
        source: e.source,
        target: e.target,
        animated: true,
        style: { stroke: '#00f0ff', strokeWidth: 1.5 },
      }))

    // Apply dagre layout
    return applyDagreLayout(flowNodes, flowEdges)
  }, [dag, selectedNodeId, selectedNodeKind, viewLevel, getUpstreamNodesByLevel])
  // Note: nodeExplanation is passed as prop but not needed for sub-graph computation

  // Compute max possible level for level control bounds
  const maxLevel = useMemo(() => {
    if (!dag || !selectedNodeId) return 0
    const allParents = getUpstreamNodesByLevel(selectedNodeId, dag)
    if (allParents.size === 0) return 0
    return Math.max(0, ...Array.from(allParents.values())) + 1
  }, [dag, selectedNodeId, getUpstreamNodesByLevel])

  // Handle node click in sub-graph
  const handleNodeClick = useCallback(
    (_: unknown, node: Node) => {
      if (node.id !== selectedNodeId) {
        onNodeSelect(node.id)
      }
    },
    [selectedNodeId, onNodeSelect]
  )

  // Fit view when nodes change
  useEffect(() => {
    if (reactFlowInstance.current && nodes.length > 0) {
      setTimeout(() => {
        reactFlowInstance.current?.fitView({ padding: 0.3, duration: 200 })
      }, 50)
    }
  }, [nodes])

  // Zoom controls
  const handleZoomIn = () => reactFlowInstance.current?.zoomIn({ duration: 200 })
  const handleZoomOut = () => reactFlowInstance.current?.zoomOut({ duration: 200 })

  return (
    <div className="flex flex-col h-full">
      {/* View Level Control */}
      <div className="flex items-center justify-between px-3 py-2 border-b border-border-subtle bg-bg-elevated/30">
        <span className="text-[10px] text-text-muted font-mono">View Level</span>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setViewLevel(Math.max(0, viewLevel - 1))}
            disabled={viewLevel === 0}
            className="p-1 rounded hover:bg-bg-elevated disabled:opacity-30 transition-colors"
            title="Decrease level"
          >
            <Minus className="size-3 text-text-muted" />
          </button>
          <span className="w-6 text-center text-xs font-mono text-neon-cyan">{viewLevel}</span>
          <button
            onClick={() => setViewLevel(Math.min(maxLevel, viewLevel + 1))}
            disabled={viewLevel >= maxLevel}
            className="p-1 rounded hover:bg-bg-elevated disabled:opacity-30 transition-colors"
            title="Increase level"
          >
            <Plus className="size-3 text-text-muted" />
          </button>
          <button
            onClick={() => setViewLevel(1)}
            className="p-1 rounded hover:bg-bg-elevated ml-1 transition-colors"
            title="Reset to level 1"
          >
            <RotateCcw className="size-3 text-text-muted" />
          </button>
        </div>
      </div>

      {/* Level indicator text */}
      <div className="flex items-center justify-between px-3 py-1.5 bg-bg-base/50 border-b border-border-subtle">
        <span className="text-[9px] text-text-dimmed font-mono">
          {viewLevel === 0 && 'Current node only'}
          {viewLevel === 1 && 'Current + direct parents'}
          {viewLevel >= 2 && `Current + ${viewLevel} levels of parents`}
        </span>
        <span className="text-[9px] text-text-muted font-mono">
          {nodes.length} node{nodes.length !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Zoom Controls */}
      <div className="flex items-center justify-end gap-1 px-2 py-1 border-b border-border-subtle">
        <button
          onClick={handleZoomOut}
          className="p-1 rounded hover:bg-bg-elevated transition-colors"
          title="Zoom out"
        >
          <ZoomOut className="size-3 text-text-muted" />
        </button>
        <button
          onClick={handleZoomIn}
          className="p-1 rounded hover:bg-bg-elevated transition-colors"
          title="Zoom in"
        >
          <ZoomIn className="size-3 text-text-muted" />
        </button>
      </div>

      {/* Mini ReactFlow */}
      <div className="flex-1 min-h-[200px]">
        {nodes.length === 0 ? (
          <div className="flex items-center justify-center h-full text-text-dimmed text-xs font-mono">
            No nodes to display
          </div>
        ) : (
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            onNodeClick={handleNodeClick}
            onInit={(instance) => {
              reactFlowInstance.current = instance
              instance.fitView({ padding: 0.3 })
            }}
            fitView
            fitViewOptions={{ padding: 0.3, maxZoom: 1.5 }}
            panOnDrag
            zoomOnScroll
            zoomOnPinch
            minZoom={0.3}
            maxZoom={2}
            proOptions={{ hideAttribution: true }}
            style={{ background: 'transparent' }}
          >
            <Background
              variant={BackgroundVariant.Dots}
              gap={16}
              size={1}
              color="rgba(0, 255, 136, 0.08)"
            />
          </ReactFlow>
        )}
      </div>

      {/* Legend */}
      <div className="flex items-center gap-4 px-3 py-2 border-t border-border-subtle bg-bg-elevated/30 text-[9px] text-text-dimmed font-mono">
        <div className="flex items-center gap-1">
          <div
            className="w-3 h-3 rounded-full"
            style={{ background: NODE_COLORS.Plan.bg }}
          />
          <span>Plan</span>
        </div>
        <div className="flex items-center gap-1">
          <div
            className="w-3 h-3 rounded-full"
            style={{ background: NODE_COLORS.Knowledge.bg }}
          />
          <span>Knowledge</span>
        </div>
        <span className="text-text-muted ml-auto">Click node to view details</span>
      </div>
    </div>
  )
}

export default SubGraphViewer
