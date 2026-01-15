import { useCallback, useMemo } from 'react'
import { useSisyphusStore, type HighlightMode } from '@/store/sisyphus-store'
import type { DAGGraph } from '@/types'

/**
 * Hook for DAG interaction logic (US5 - Highlight Related Nodes)
 * Provides utilities for computing upstream/downstream node chains
 */
export function useDagInteractions() {
  const {
    dag,
    selectedNodeId,
    highlightedNodeIds,
    highlightMode,
    setHighlightedNodeIds,
    setHighlightMode,
  } = useSisyphusStore()

  /**
   * Get all upstream nodes (dependencies) for a given node using BFS
   */
  const getUpstreamNodeIds = useCallback(
    (nodeId: string, graph: DAGGraph | null): string[] => {
      if (!graph?.nodes || !graph?.edges) return []

      const visited = new Set<string>()
      const queue: string[] = []
      const result: string[] = []

      // Find direct dependencies (edges where this node is the target)
      const directDeps = graph.edges
        .filter((e) => e.target === nodeId)
        .map((e) => e.source)

      queue.push(...directDeps)

      while (queue.length > 0) {
        const current = queue.shift()!
        if (visited.has(current)) continue
        visited.add(current)
        result.push(current)

        // Find dependencies of current node
        const deps = graph.edges
          .filter((e) => e.target === current)
          .map((e) => e.source)

        queue.push(...deps)
      }

      return result
    },
    []
  )

  /**
   * Get all downstream nodes (dependents) for a given node using BFS
   */
  const getDownstreamNodeIds = useCallback(
    (nodeId: string, graph: DAGGraph | null): string[] => {
      if (!graph?.nodes || !graph?.edges) return []

      const visited = new Set<string>()
      const queue: string[] = []
      const result: string[] = []

      // Find direct dependents (edges where this node is the source)
      const directDependents = graph.edges
        .filter((e) => e.source === nodeId)
        .map((e) => e.target)

      queue.push(...directDependents)

      while (queue.length > 0) {
        const current = queue.shift()!
        if (visited.has(current)) continue
        visited.add(current)
        result.push(current)

        // Find dependents of current node
        const dependents = graph.edges
          .filter((e) => e.source === current)
          .map((e) => e.target)

        queue.push(...dependents)
      }

      return result
    },
    []
  )

  /**
   * Get the full knowledge chain (both upstream and downstream)
   */
  const getFullChainNodeIds = useCallback(
    (nodeId: string, graph: DAGGraph | null): string[] => {
      const upstream = getUpstreamNodeIds(nodeId, graph)
      const downstream = getDownstreamNodeIds(nodeId, graph)
      return [...new Set([...upstream, ...downstream])]
    },
    [getUpstreamNodeIds, getDownstreamNodeIds]
  )

  /**
   * Set highlight mode and compute highlighted nodes
   */
  const setHighlight = useCallback(
    (nodeId: string | null, mode: HighlightMode) => {
      if (!nodeId || mode === 'none') {
        setHighlightMode('none')
        setHighlightedNodeIds([])
        return
      }

      setHighlightMode(mode)

      switch (mode) {
        case 'upstream':
          setHighlightedNodeIds(getUpstreamNodeIds(nodeId, dag))
          break
        case 'downstream':
          setHighlightedNodeIds(getDownstreamNodeIds(nodeId, dag))
          break
        case 'chain':
          setHighlightedNodeIds(getFullChainNodeIds(nodeId, dag))
          break
        default:
          setHighlightedNodeIds([])
      }
    },
    [dag, getUpstreamNodeIds, getDownstreamNodeIds, getFullChainNodeIds, setHighlightMode, setHighlightedNodeIds]
  )

  /**
   * Clear all highlights
   */
  const clearHighlight = useCallback(() => {
    setHighlightMode('none')
    setHighlightedNodeIds([])
  }, [setHighlightMode, setHighlightedNodeIds])

  /**
   * Check if a node is highlighted
   */
  const isNodeHighlighted = useCallback(
    (nodeId: string): boolean => {
      if (highlightMode === 'none') return false
      return highlightedNodeIds.includes(nodeId)
    },
    [highlightMode, highlightedNodeIds]
  )

  /**
   * Get opacity for a node based on highlight state
   */
  const getNodeOpacity = useCallback(
    (nodeId: string, isSelected: boolean): number => {
      if (highlightMode === 'none') return 1
      if (isSelected) return 1
      if (selectedNodeId === nodeId) return 1
      if (isNodeHighlighted(nodeId)) return 1
      return 0.3 // Dimmed
    },
    [highlightMode, selectedNodeId, isNodeHighlighted]
  )

  /**
   * Statistics about the current DAG
   */
  const dagStats = useMemo(() => {
    if (!dag?.nodes) return { planCount: 0, knowledgeCount: 0, totalCount: 0 }

    const planNodes = dag.nodes.filter((n) => n.kind === 'Plan')
    const knowledgeNodes = dag.nodes.filter((n) => n.kind === 'Knowledge')

    return {
      planCount: planNodes.length,
      knowledgeCount: knowledgeNodes.length,
      totalCount: dag.nodes.length,
    }
  }, [dag])

  return {
    getUpstreamNodeIds,
    getDownstreamNodeIds,
    getFullChainNodeIds,
    setHighlight,
    clearHighlight,
    isNodeHighlighted,
    getNodeOpacity,
    highlightMode,
    highlightedNodeIds,
    dagStats,
  }
}
