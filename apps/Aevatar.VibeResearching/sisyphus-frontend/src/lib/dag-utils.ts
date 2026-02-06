// ============================================================
//  DAG Utilities - Shared helpers for DAG data processing
// ============================================================

import type { NodeKind } from '@/types'

/**
 * Normalize node kind from backend to standard format
 * Handles: undefined, null, empty string, lowercase variants
 * 
 * Backend may return: 'Plan', 'plan', 'Knowledge', 'knowledge', undefined, null, ''
 * Frontend expects: 'Plan' | 'Knowledge' | undefined
 */
export function normalizeNodeKind(kind: string | undefined | null): NodeKind | undefined {
  if (!kind) return undefined
  const normalized = kind.trim().toLowerCase()
  if (normalized === 'plan') return 'Plan'
  if (normalized === 'knowledge') return 'Knowledge'
  return undefined
}
