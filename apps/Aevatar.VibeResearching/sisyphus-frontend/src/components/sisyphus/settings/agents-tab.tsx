// ============================================================================
//  Settings Panel - Agents Configuration Tab
// ============================================================================

import React, { useEffect, useState, useMemo, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { getAgentProviders, setAgentProvider } from '@/lib/axiom-client'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { AgentProvidersResponse, SetAgentProviderResponse } from './types'

interface AgentsTabProps {
  sessionId: string | null
  connected: boolean
  agentRoster: Array<{ agent: string; agentId?: string }>
  availableProviders: string[]
}

const FALLBACK_AGENT_ROSTER = [
  'research_assistant',
  'planner',
  'reasoner',
  'librarian',
  'verifier',
  'dag_builder',
  'paper_editor',
]

export const AgentsTab: React.FC<AgentsTabProps> = ({
  sessionId,
  connected,
  agentRoster,
  availableProviders,
}) => {
  const [agentProviderMap, setAgentProviderMap] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [msg, setMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  const effectiveRoster = useMemo(() => {
    if (agentRoster.length > 0) {
      return agentRoster
    }
    return FALLBACK_AGENT_ROSTER.map((agent) => ({ agent }))
  }, [agentRoster])

  useEffect(() => {
    if (!sessionId || !connected) return
    const fetch = async () => {
      setLoading(true)
      try {
        const res = (await getAgentProviders(sessionId)) as AgentProvidersResponse
        if (res?.map) {
          setAgentProviderMap(res.map)
        }
      } catch {
        /* best-effort */
      }
      setLoading(false)
    }
    fetch()
  }, [sessionId, connected])

  const handleSetAgentProvider = useCallback(
    async (agent: string, providerName: string) => {
      if (!sessionId || busy) return
      setBusy(agent)
      setMsg(null)
      try {
        const res = (await setAgentProvider(
          sessionId,
          agent,
          providerName
        )) as SetAgentProviderResponse
        if (res?.map) {
          setAgentProviderMap(res.map)
          setMsg({ kind: 'ok', text: `${agent} → ${providerName || 'default'}` })
        } else if (res?.error) {
          setMsg({ kind: 'err', text: res.error })
        }
      } catch (e) {
        const err = e as Error
        setMsg({ kind: 'err', text: err?.message || 'Failed to set provider' })
      }
      setBusy(null)
    },
    [sessionId, busy]
  )

  if (!sessionId) {
    return (
      <div className="card p-8 text-center">
        <div className="text-4xl mb-4">🔗</div>
        <h3 className="text-lg font-display font-semibold text-text-primary mb-2">
          No Session Connected
        </h3>
        <p className="text-sm text-text-muted">
          Connect to a session to configure per-agent LLM providers.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Info Card */}
      <div className="card p-5">
        <h3 className="text-sm font-display font-semibold text-neon-cyan tracking-wider mb-2">
          Per-Agent Provider Configuration
        </h3>
        <p className="text-xs text-text-muted">
          Each agent can use a different LLM provider. Leave as "default" to use the global default
          provider.
        </p>
        {msg && (
          <div
            className={cn(
              'mt-3 px-3 py-2 rounded-lg text-xs font-mono',
              msg.kind === 'ok'
                ? 'bg-neon-green/10 text-neon-green border border-neon-green/30'
                : 'bg-neon-red/10 text-neon-red border border-neon-red/30'
            )}
          >
            {msg.text}
          </div>
        )}
      </div>

      {/* Agents List */}
      <div className="card overflow-hidden">
        <div className="px-4 py-3 border-b border-border-subtle bg-surface-elevated/50">
          <div className="flex items-center justify-between">
            <span className="text-xs font-mono text-text-dimmed uppercase tracking-wider">
              Agents ({effectiveRoster.length})
            </span>
            {loading && <span className="text-xs text-text-muted">Loading...</span>}
          </div>
        </div>

        <div className="divide-y divide-border-subtle">
          {effectiveRoster.map((item) => {
            const agent = item.agent
            const currentProvider = agentProviderMap[agent] || ''
            const isUpdating = busy === agent

            return (
              <div
                key={agent}
                className="px-4 py-3 hover:bg-surface-elevated/30 transition-colors"
              >
                <div className="flex items-center justify-between gap-4">
                  {/* Agent Info */}
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <div className="size-8 rounded-lg bg-neon-cyan/10 border border-neon-cyan/30 flex items-center justify-center">
                        <span className="text-xs font-bold text-neon-cyan uppercase">
                          {agent.slice(0, 2)}
                        </span>
                      </div>
                      <div>
                        <div className="text-sm font-mono text-text-primary">{agent}</div>
                      </div>
                    </div>
                  </div>

                  {/* Provider Selector */}
                  <div className="flex items-center gap-2">
                    <Select
                      value={currentProvider || '__default__'}
                      onValueChange={(v) =>
                        handleSetAgentProvider(agent, v === '__default__' ? '' : v)
                      }
                      disabled={isUpdating || !connected}
                    >
                      <SelectTrigger className={cn('w-48 text-sm', isUpdating && 'opacity-50')}>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="__default__">default</SelectItem>
                        {availableProviders.map((p) => (
                          <SelectItem key={p} value={p}>
                            {p}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    {isUpdating && (
                      <svg
                        className="size-4 animate-spin text-neon-cyan"
                        fill="none"
                        viewBox="0 0 24 24"
                      >
                        <circle
                          className="opacity-25"
                          cx="12"
                          cy="12"
                          r="10"
                          stroke="currentColor"
                          strokeWidth="4"
                        />
                        <path
                          className="opacity-75"
                          fill="currentColor"
                          d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                        />
                      </svg>
                    )}
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      </div>

      {/* Hint */}
      <div className="text-[10px] text-text-dimmed font-mono px-1">
        Agent provider mappings are stored per-session in artifacts/ui/agent_providers.json
      </div>
    </div>
  )
}
