// ============================================================================
//  New Session Dialog
//  Shows per-agent provider selection before creating a research session.
//  Reuses <ProviderSelector> and fetches providers via sessionless endpoint.
// ============================================================================

import React, { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogFooter,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog'
import { ProviderSelector } from '@/components/user-providers/ProviderSelector'
import { getAvailableProvidersForUser } from '@/lib/axiom-client/user-provider'
import { createSession } from '@/lib/axiom-client/session'
import type { AvailableProviderDto } from '@/types/user-provider'
import { cn } from '@/lib/utils'

/** The 7 default agents known before any session exists. */
const DEFAULT_AGENTS = [
  'research_assistant',
  'planner',
  'reasoner',
  'librarian',
  'verifier',
  'dag_builder',
  'paper_editor',
] as const

/** Human-friendly labels for each agent. */
const AGENT_LABELS: Record<string, string> = {
  research_assistant: 'Research Assistant',
  planner: 'Planner',
  reasoner: 'Reasoner',
  librarian: 'Librarian',
  verifier: 'Verifier',
  dag_builder: 'DAG Builder',
  paper_editor: 'Paper Editor',
}

interface NewSessionDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onCreated: (sessionId: string) => void
}

/**
 * Dialog for creating a new research session with per-agent provider selection.
 * Fetches available providers on open, lets the user assign providers per agent,
 * then creates the session with initial mappings.
 */
const NewSessionDialog: React.FC<NewSessionDialogProps> = ({
  open,
  onOpenChange,
  onCreated,
}) => {
  const [providers, setProviders] = useState<AvailableProviderDto[]>([])
  const [loading, setLoading] = useState(false)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Per-agent provider selection: agent name → namespace (empty = default)
  const [agentMap, setAgentMap] = useState<Record<string, string>>(() =>
    Object.fromEntries(DEFAULT_AGENTS.map((a) => [a, '']))
  )

  // Bulk-apply value
  const [bulkProvider, setBulkProvider] = useState('')

  // Fetch providers when dialog opens
  useEffect(() => {
    if (!open) return
    let cancelled = false
    setLoading(true)
    setError(null)

    getAvailableProvidersForUser()
      .then((res) => {
        if (!cancelled) setProviders(res.providers ?? [])
      })
      .catch((err) => {
        if (!cancelled) setError(err?.message ?? 'Failed to load providers')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => { cancelled = true }
  }, [open])

  // Reset state when dialog closes
  useEffect(() => {
    if (!open) {
      setAgentMap(Object.fromEntries(DEFAULT_AGENTS.map((a) => [a, ''])))
      setBulkProvider('')
      setError(null)
      setCreating(false)
    }
  }, [open])

  const handleAgentChange = useCallback((agent: string, namespace: string) => {
    setAgentMap((prev) => ({ ...prev, [agent]: namespace }))
  }, [])

  const handleBulkApply = useCallback((namespace: string) => {
    setBulkProvider(namespace)
    setAgentMap(Object.fromEntries(DEFAULT_AGENTS.map((a) => [a, namespace])))
  }, [])

  // Build the initial provider map (only non-empty entries)
  const initialAgentProviders = useMemo(() => {
    const map: Record<string, string> = {}
    for (const [agent, ns] of Object.entries(agentMap)) {
      if (ns) map[agent] = ns
    }
    return map
  }, [agentMap])

  const handleCreate = useCallback(async () => {
    setCreating(true)
    setError(null)
    try {
      const result = await createSession(undefined, initialAgentProviders)
      if (result.ok && result.sessionId) {
        onOpenChange(false)
        onCreated(result.sessionId)
      } else {
        setError(result.error ?? 'Failed to create session')
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Unexpected error')
    } finally {
      setCreating(false)
    }
  }, [initialAgentProviders, onCreated, onOpenChange])

  const handleQuickStart = useCallback(async () => {
    setCreating(true)
    setError(null)
    try {
      const result = await createSession()
      if (result.ok && result.sessionId) {
        onOpenChange(false)
        onCreated(result.sessionId)
      } else {
        setError(result.error ?? 'Failed to create session')
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Unexpected error')
    } finally {
      setCreating(false)
    }
  }, [onCreated, onOpenChange])

  const hasCustomSelections = Object.values(agentMap).some(Boolean)

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg bg-[#0a0e17]/98 backdrop-blur-xl border border-neon-cyan/20 rounded-xl shadow-[0_0_40px_rgba(0,255,249,0.08)] p-0 overflow-hidden">
        {/* Accent bar */}
        <div className="h-[2px] bg-gradient-to-r from-transparent via-neon-cyan/60 to-transparent" />

        <div className="px-5 pt-5 pb-2">
          <DialogHeader className="gap-2">
            <div className="flex items-center gap-2.5">
              <div className="flex items-center justify-center size-9 rounded-lg bg-neon-cyan/10 border border-neon-cyan/20">
                <svg className="size-4 text-neon-cyan" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
                </svg>
              </div>
              <DialogTitle className="text-sm font-display text-text-primary tracking-wide">
                New Research Session
              </DialogTitle>
            </div>
            <DialogDescription className="text-[11px] leading-relaxed text-text-muted pl-[46px]">
              Choose which LLM provider each agent uses, or Quick Start with defaults.
            </DialogDescription>
          </DialogHeader>
        </div>

        {/* Body */}
        <div className="px-5 pb-2 max-h-[60vh] overflow-y-auto">
          {loading ? (
            <div className="flex items-center justify-center py-10">
              <div className="size-5 border-2 border-neon-cyan/30 border-t-neon-cyan rounded-full animate-spin" />
              <span className="ml-3 text-xs font-mono text-text-muted">Loading providers...</span>
            </div>
          ) : error && providers.length === 0 ? (
            <div className="py-6 text-center">
              <p className="text-xs text-neon-red font-mono">{error}</p>
              <button
                onClick={() => { setError(null); setLoading(true); getAvailableProvidersForUser().then(r => setProviders(r.providers ?? [])).catch(() => setError('Retry failed')).finally(() => setLoading(false)) }}
                className="mt-2 text-[10px] font-mono text-neon-cyan hover:underline"
              >
                Retry
              </button>
            </div>
          ) : (
            <>
              {/* Bulk apply */}
              <div className="mb-4 p-3 rounded-lg bg-surface/50 border border-border-subtle">
                <div className="flex items-center justify-between mb-1.5">
                  <span className="text-[10px] font-mono text-neon-gold uppercase tracking-wider">
                    Apply to all agents
                  </span>
                  {hasCustomSelections && (
                    <button
                      onClick={() => {
                        setBulkProvider('')
                        setAgentMap(Object.fromEntries(DEFAULT_AGENTS.map((a) => [a, ''])))
                      }}
                      className="text-[9px] font-mono text-text-muted hover:text-neon-cyan transition-colors"
                    >
                      Reset all
                    </button>
                  )}
                </div>
                <ProviderSelector
                  value={bulkProvider}
                  onChange={handleBulkApply}
                  providers={providers}
                  className="w-full"
                />
              </div>

              {/* Per-agent table */}
              <div className="space-y-2">
                {DEFAULT_AGENTS.map((agent) => (
                  <div
                    key={agent}
                    className="flex items-center gap-3 px-3 py-2 rounded-lg bg-surface/30 border border-border-subtle/50 hover:border-border-default transition-colors"
                  >
                    <div className="flex-shrink-0 w-32">
                      <span className="text-xs font-mono text-text-primary">
                        {AGENT_LABELS[agent] ?? agent}
                      </span>
                      <div className="text-[9px] font-mono text-text-dimmed">{agent}</div>
                    </div>
                    <div className="flex-1 min-w-0">
                      <ProviderSelector
                        value={agentMap[agent] ?? ''}
                        onChange={(ns) => handleAgentChange(agent, ns)}
                        providers={providers}
                        className="w-full"
                      />
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}

          {/* Error banner */}
          {error && providers.length > 0 && (
            <div className="mt-3 px-3 py-2 rounded-lg bg-neon-red/5 border border-neon-red/20">
              <span className="text-[10px] font-mono text-neon-red">{error}</span>
            </div>
          )}
        </div>

        {/* Footer */}
        <DialogFooter className="px-5 pb-5 pt-3 flex-row justify-between gap-2 border-t border-border-subtle/30">
          <button
            onClick={handleQuickStart}
            disabled={creating}
            className={cn(
              'px-4 py-2 rounded-lg text-[11px] font-mono transition-all',
              'text-text-secondary bg-transparent hover:bg-surface-elevated border border-[#2a3040] hover:border-border-default',
              creating && 'opacity-50 cursor-not-allowed'
            )}
          >
            Quick Start
          </button>
          <button
            onClick={handleCreate}
            disabled={creating || loading}
            className={cn(
              'px-5 py-2 rounded-lg text-[11px] font-mono font-semibold transition-all',
              'text-bg-base bg-neon-cyan hover:bg-neon-cyan/90 border border-neon-cyan/60',
              'hover:shadow-[0_0_16px_rgba(0,255,249,0.3)]',
              (creating || loading) && 'opacity-50 cursor-not-allowed'
            )}
          >
            {creating ? 'Creating...' : 'Create Session'}
          </button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export default NewSessionDialog
