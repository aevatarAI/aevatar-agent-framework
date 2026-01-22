// ============================================================
//  SubGraphViewer - Parent Chain List View
//  Shows derivation/plan chain as a hierarchical list
// ============================================================

import { useState, useMemo } from 'react'
import { ChevronRight, Minus, Plus, RotateCcw } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { DAGGraph, NodeKind } from '@/types'
import type { NodeExplanationData } from '@/store/sisyphus-store'
import { useDagInteractions } from '@/hooks/use-dag-interactions'

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

interface SubGraphViewerProps {
  dag: DAGGraph | null
  selectedNodeId: string
  selectedNodeKind: 'Plan' | 'Knowledge'
  nodeExplanation?: NodeExplanationData | null
  onNodeSelect: (nodeId: string) => void
}

interface ParentNode {
  id: string
  label: string
  kind: NodeKind
  level: number
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
// Node Item Component
// ─────────────────────────────────────────────────────────────

interface NodeItemProps {
  node: ParentNode
  isSelected: boolean
  onClick: () => void
}

function NodeItem({ node, isSelected, onClick }: NodeItemProps) {
  const colors = NODE_COLORS[node.kind] || NODE_COLORS.Knowledge

  return (
    <button
      onClick={onClick}
      className={cn(
        "w-full flex items-center gap-3 px-3 py-2 rounded-lg transition-all text-left",
        "hover:bg-bg-elevated/50",
        isSelected && "bg-bg-elevated border border-neon-gold/40"
      )}
    >
      {/* Level indicator */}
      <div className="flex items-center gap-1 text-text-dimmed">
        {Array.from({ length: node.level }).map((_, i) => (
          <ChevronRight key={i} className="size-3 opacity-40" />
        ))}
      </div>

      {/* Node circle */}
      <div
        className={cn(
          "flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center",
          isSelected && "ring-2 ring-neon-gold ring-offset-1 ring-offset-bg-base"
        )}
        style={{
          background: `radial-gradient(circle, ${colors.bg} 0%, ${colors.border} 100%)`,
          boxShadow: isSelected
            ? `0 0 16px ${colors.glow}`
            : `0 0 8px ${colors.glow}`,
        }}
      >
        <span className="text-xs" style={{ color: '#0a0f19' }}>
          {node.kind === 'Plan' ? '📋' : '💡'}
        </span>
      </div>

      {/* Node info */}
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <span className={cn(
            "text-[9px] px-1.5 py-0.5 rounded shrink-0",
            node.kind === 'Plan' ? "bg-blue-500/20 text-blue-400" : "bg-green-500/20 text-green-400"
          )}>
            {node.kind}
          </span>
          <span className="text-[10px] text-text-dimmed">L{node.level}</span>
        </div>
        <div className="text-xs font-mono text-text-secondary truncate mt-0.5" title={node.id}>
          {node.id.length > 20 ? `${node.id.slice(0, 10)}...${node.id.slice(-8)}` : node.id}
        </div>
        {node.label && node.label !== node.id && (
          <div className="text-[10px] text-text-muted truncate" title={node.label}>
            {node.label.length > 30 ? `${node.label.slice(0, 28)}...` : node.label}
          </div>
        )}
      </div>
    </button>
  )
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
  const [viewLevel, setViewLevel] = useState(2)
  const { getUpstreamNodesByLevel } = useDagInteractions()

  // Calculate parent nodes with levels
  const { parentNodes, maxLevel } = useMemo(() => {
    if (!dag || !selectedNodeId) return { parentNodes: [], maxLevel: 0 }

    // Get all parent nodes with their levels
    const parentsByLevel = getUpstreamNodesByLevel(selectedNodeId, dag)
    
    // Compute max level
    const computedMaxLevel = parentsByLevel.size === 0 
      ? 0 
      : Math.max(0, ...Array.from(parentsByLevel.values())) + 1

    // Build list of nodes within view level
    const nodes: ParentNode[] = []

    // Add selected node at level 0
    const selectedDagNode = dag.nodes.find(n => n.id === selectedNodeId)
    if (selectedDagNode) {
      nodes.push({
        id: selectedDagNode.id,
        label: selectedDagNode.label || selectedDagNode.id,
        kind: selectedNodeKind,
        level: 0,
      })
    }

    // Add parent nodes within view level
    if (viewLevel > 0) {
      parentsByLevel.forEach((level, nodeId) => {
        if (level < viewLevel) {
          const dagNode = dag.nodes.find(n => n.id === nodeId)
          if (dagNode) {
            nodes.push({
              id: dagNode.id,
              label: dagNode.label || dagNode.id,
              kind: (dagNode.kind as NodeKind) || 'Knowledge',
              level: level + 1,
            })
          }
        }
      })
    }

    // Sort by level
    nodes.sort((a, b) => a.level - b.level)

    return { parentNodes: nodes, maxLevel: computedMaxLevel }
  }, [dag, selectedNodeId, selectedNodeKind, viewLevel, getUpstreamNodesByLevel])

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
            onClick={() => setViewLevel(2)}
            className="p-1 rounded hover:bg-bg-elevated ml-1 transition-colors"
            title="Reset to level 2"
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
          {parentNodes.length} node{parentNodes.length !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Node List */}
      <div className="flex-1 overflow-y-auto p-2 space-y-1">
        {parentNodes.length === 0 ? (
          <div className="flex items-center justify-center h-full text-text-dimmed text-xs font-mono">
            No nodes to display
          </div>
        ) : (
          parentNodes.map(node => (
            <NodeItem
              key={node.id}
              node={node}
              isSelected={node.id === selectedNodeId}
              onClick={() => {
                if (node.id !== selectedNodeId) {
                  onNodeSelect(node.id)
                }
              }}
            />
          ))
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
        <span className="text-text-muted ml-auto">Click to view details</span>
      </div>
    </div>
  )
}

export default SubGraphViewer
