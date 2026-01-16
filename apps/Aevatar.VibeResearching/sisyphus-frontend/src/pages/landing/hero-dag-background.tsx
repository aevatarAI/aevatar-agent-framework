// ============================================================
//  Hero DAG Background - Decorative Animated Mesh
// ============================================================

import { useMemo } from 'react'
import { motion } from 'framer-motion'

// ─── Node Configuration ───
interface HeroNode {
  id: string
  x: number
  y: number
  r: number
  delay: number
}

interface HeroEdge {
  from: string
  to: string
}

export function HeroDagBackground() {
  // Generate random-ish but deterministic node positions
  const { nodes, edges } = useMemo(() => {
    const nodeData: HeroNode[] = [
      { id: 'n1', x: 15, y: 20, r: 4, delay: 0 },
      { id: 'n2', x: 30, y: 35, r: 5, delay: 0.2 },
      { id: 'n3', x: 25, y: 60, r: 3, delay: 0.4 },
      { id: 'n4', x: 45, y: 25, r: 6, delay: 0.1 },
      { id: 'n5', x: 55, y: 50, r: 4, delay: 0.3 },
      { id: 'n6', x: 70, y: 30, r: 5, delay: 0.5 },
      { id: 'n7', x: 75, y: 55, r: 3, delay: 0.2 },
      { id: 'n8', x: 85, y: 40, r: 4, delay: 0.4 },
      { id: 'n9', x: 40, y: 70, r: 5, delay: 0.6 },
      { id: 'n10', x: 60, y: 75, r: 4, delay: 0.1 },
      { id: 'n11', x: 20, y: 80, r: 3, delay: 0.3 },
      { id: 'n12', x: 80, y: 70, r: 4, delay: 0.5 },
      { id: 'n13', x: 50, y: 15, r: 3, delay: 0.2 },
      { id: 'n14', x: 90, y: 20, r: 4, delay: 0.4 },
      { id: 'n15', x: 10, y: 45, r: 3, delay: 0.6 },
    ]

    const edgeData: HeroEdge[] = [
      { from: 'n1', to: 'n2' },
      { from: 'n2', to: 'n3' },
      { from: 'n2', to: 'n4' },
      { from: 'n4', to: 'n5' },
      { from: 'n4', to: 'n6' },
      { from: 'n5', to: 'n7' },
      { from: 'n6', to: 'n8' },
      { from: 'n3', to: 'n9' },
      { from: 'n5', to: 'n10' },
      { from: 'n9', to: 'n10' },
      { from: 'n3', to: 'n11' },
      { from: 'n7', to: 'n12' },
      { from: 'n4', to: 'n13' },
      { from: 'n6', to: 'n14' },
      { from: 'n1', to: 'n15' },
      { from: 'n15', to: 'n3' },
      { from: 'n13', to: 'n6' },
      { from: 'n10', to: 'n12' },
    ]

    return { nodes: nodeData, edges: edgeData }
  }, [])

  // Get node position by ID
  const getNodePos = (id: string) => {
    const node = nodes.find(n => n.id === id)
    return node ? { x: node.x, y: node.y } : { x: 0, y: 0 }
  }

  return (
    <div className="absolute inset-0 overflow-hidden pointer-events-none">
      <svg
        className="w-full h-full"
        viewBox="0 0 100 100"
        preserveAspectRatio="xMidYMid slice"
      >
        <defs>
          {/* Glow Filter */}
          <filter id="glow" x="-50%" y="-50%" width="200%" height="200%">
            <feGaussianBlur stdDeviation="0.5" result="coloredBlur" />
            <feMerge>
              <feMergeNode in="coloredBlur" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>

          {/* Gradient for edges */}
          <linearGradient id="edgeGradient" x1="0%" y1="0%" x2="100%" y2="0%">
            <stop offset="0%" stopColor="rgba(125, 211, 252, 0.3)" />
            <stop offset="50%" stopColor="rgba(125, 211, 252, 0.6)" />
            <stop offset="100%" stopColor="rgba(125, 211, 252, 0.3)" />
          </linearGradient>
        </defs>

        {/* ─── Edges ─── */}
        <g className="opacity-30">
          {edges.map((edge, i) => {
            const from = getNodePos(edge.from)
            const to = getNodePos(edge.to)
            return (
              <motion.line
                key={`edge-${i}`}
                x1={from.x}
                y1={from.y}
                x2={to.x}
                y2={to.y}
                stroke="url(#edgeGradient)"
                strokeWidth="0.15"
                initial={{ pathLength: 0, opacity: 0 }}
                animate={{ pathLength: 1, opacity: 1 }}
                transition={{ duration: 2, delay: i * 0.1, ease: 'easeOut' }}
              />
            )
          })}
        </g>

        {/* ─── Animated Edge Particles ─── */}
        <g className="opacity-60">
          {edges.slice(0, 8).map((edge, i) => {
            const from = getNodePos(edge.from)
            const to = getNodePos(edge.to)
            return (
              <motion.circle
                key={`particle-${i}`}
                r="0.3"
                fill="#7dd3fc"
                filter="url(#glow)"
                initial={{ cx: from.x, cy: from.y }}
                animate={{
                  cx: [from.x, to.x, from.x],
                  cy: [from.y, to.y, from.y],
                }}
                transition={{
                  duration: 4 + i * 0.5,
                  repeat: Infinity,
                  ease: 'linear',
                  delay: i * 0.3,
                }}
              />
            )
          })}
        </g>

        {/* ─── Nodes ─── */}
        <g>
          {nodes.map((node) => (
            <g key={node.id}>
              {/* Outer ring */}
              <motion.circle
                cx={node.x}
                cy={node.y}
                r={node.r + 1}
                fill="none"
                stroke="rgba(125, 211, 252, 0.2)"
                strokeWidth="0.1"
                initial={{ scale: 0, opacity: 0 }}
                animate={{ scale: 1, opacity: 1 }}
                transition={{ duration: 0.5, delay: node.delay }}
              />
              
              {/* Inner glow */}
              <motion.circle
                cx={node.x}
                cy={node.y}
                r={node.r}
                fill="rgba(125, 211, 252, 0.1)"
                filter="url(#glow)"
                initial={{ scale: 0, opacity: 0 }}
                animate={{ 
                  scale: [1, 1.1, 1],
                  opacity: [0.3, 0.5, 0.3],
                }}
                transition={{
                  duration: 3,
                  repeat: Infinity,
                  delay: node.delay,
                  ease: 'easeInOut',
                }}
              />
              
              {/* Core */}
              <motion.circle
                cx={node.x}
                cy={node.y}
                r={node.r * 0.4}
                fill="rgba(125, 211, 252, 0.6)"
                initial={{ scale: 0, opacity: 0 }}
                animate={{ scale: 1, opacity: 1 }}
                transition={{ duration: 0.3, delay: node.delay + 0.2 }}
              />
            </g>
          ))}
        </g>
      </svg>
    </div>
  )
}
