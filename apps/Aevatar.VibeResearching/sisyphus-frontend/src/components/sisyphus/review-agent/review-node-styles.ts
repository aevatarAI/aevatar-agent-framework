// ============================================================
//  Review Agent Node Styles
//  Colors based on review status (for progress graph popup)
// ============================================================

export type ReviewNodeStatus =
  | 'validated'       // Node has been reviewed and passed
  | 'pending'         // Node is waiting to be reviewed
  | 'deactivated'     // Node failed verification
  | 'removed'         // Node was removed from graph
  | 'reviewing'       // Node is currently being reviewed

export interface ReviewNodeStyle {
  bg: string
  border: string
  glow: string
  textColor: string
}

// ─────────────────────────────────────────────────────────────
// Review Status Styles (US23 - Progress Counter Graph Popup)
// Green = validated/passed, Yellow = pending, Red = deactivated,
// Purple = removed, Orange + blink = currently reviewing
// ─────────────────────────────────────────────────────────────

export const REVIEW_NODE_STYLES: Record<ReviewNodeStatus, ReviewNodeStyle> = {
  validated: {
    bg: '#22c55e',      // Green-500
    border: '#4ade80',  // Green-400
    glow: 'rgba(34, 197, 94, 0.6)',
    textColor: '#0a0f19',
  },
  pending: {
    bg: '#eab308',      // Yellow-500
    border: '#facc15',  // Yellow-400
    glow: 'rgba(234, 179, 8, 0.6)',
    textColor: '#0a0f19',
  },
  deactivated: {
    bg: '#ef4444',      // Red-500
    border: '#f87171',  // Red-400
    glow: 'rgba(239, 68, 68, 0.6)',
    textColor: '#ffffff',
  },
  removed: {
    bg: '#a855f7',      // Purple-500
    border: '#c084fc',  // Purple-400
    glow: 'rgba(168, 85, 247, 0.6)',
    textColor: '#ffffff',
  },
  reviewing: {
    bg: '#f97316',      // Orange-500
    border: '#fb923c',  // Orange-400
    glow: 'rgba(249, 115, 22, 0.8)',
    textColor: '#0a0f19',
  },
}

// Opacity for different display modes
export const REVIEW_STATUS_OPACITY: Record<ReviewNodeStatus, number> = {
  validated: 1,
  pending: 0.85,
  deactivated: 0.75,
  removed: 0.5,
  reviewing: 1,
}

// ─────────────────────────────────────────────────────────────
// Types for Review Graph
// ─────────────────────────────────────────────────────────────

export interface ReviewGraphNode {
  id: string
  label: string
  reviewStatus: ReviewNodeStatus
  x?: number
  y?: number
  // Additional metadata
  sessionId?: string
  lastReviewedAt?: string | null
  deactivatedAt?: string | null
  deactivatedReason?: string | null
}

export interface ReviewGraphEdge {
  id: string
  source: string
  target: string
  type: 'depends_on' | 'motivated_by'
}

// Filter mode for review graph
export type ReviewFilterMode = 'all' | 'validated' | 'pending' | 'deactivated' | 'removed' | 'reviewing'
