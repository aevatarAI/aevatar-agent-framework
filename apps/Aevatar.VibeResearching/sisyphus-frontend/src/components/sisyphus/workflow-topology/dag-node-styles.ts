// ============================================================
//  DAG Node Styles & Types
// ============================================================

// ─────────────────────────────────────────────────────────────
// Node Style (Kind-Based: US3/FR-007/FR-008)
// Plan nodes = Blue/Cyan, Knowledge nodes = Green
// Other session nodes = Dimmed Gray, Active milestone = Orange
// ─────────────────────────────────────────────────────────────

export const NODE_STYLES = {
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
} as const

export const STATUS_OPACITY: Record<string, number> = {
  pending: 0.6,
  running: 0.85,
  completed: 1,
  error: 0.4,
}

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

export interface CyberNodeData {
  id: string
  label: string
  status: string
  selected?: boolean
  kind?: 'Plan' | 'Knowledge'
  planStatus?: 'Pending' | 'Active' | 'Completed'
  highlighted?: boolean
  dimmed?: boolean
  isOtherSession?: boolean
}

export type NodeFilterMode = 'all' | 'PlanActive' | 'Plan' | 'Knowledge' | 'OtherSession'
