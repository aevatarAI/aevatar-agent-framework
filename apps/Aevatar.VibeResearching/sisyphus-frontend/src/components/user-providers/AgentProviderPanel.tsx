// ============================================================================
//  Agent Provider Panel - Per-agent LLM provider configuration
//  Shows agents with dropdown selectors for assigning user/platform providers
// ============================================================================

import React, { useEffect, useState, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { Loader2 } from 'lucide-react'
import { useToast } from '@/components/ui/toast'
import { ProviderSelector } from './ProviderSelector'
import {
  getSessionAgentProviders,
  updateSessionAgentProviders,
  getAvailableProviders,
} from '@/lib/axiom-client/user-provider'
import { useUserProviderStore } from '@/store/user-provider-store'
import type {
  AgentProviderDetail,
} from '@/types/user-provider'

interface AgentProviderPanelProps {
  sessionId: string | null
  connected: boolean
  agentRoster: Array<{ agent: string; agentId?: string }>
}

/**
 * Panel showing per-agent provider assignments within a session.
 * Each agent row has a dropdown to select from user + platform providers.
 */
export const AgentProviderPanel: React.FC<AgentProviderPanelProps> = ({
  sessionId,
  connected,
  agentRoster,
}) => {
  const toast = useToast()
  const availableProviders = useUserProviderStore((s) => s.availableProviders)
  const setAvailableProviders = useUserProviderStore((s) => s.setAvailableProviders)
  const setAvailableLoading = useUserProviderStore((s) => s.setAvailableLoading)

  const [agentMap, setAgentMap] = useState<Record<string, AgentProviderDetail>>({})
  const [loading, setLoading] = useState(false)
  const [updatingAgent, setUpdatingAgent] = useState<string | null>(null)

  // Fetch agent providers and available providers
  useEffect(() => {
    if (!sessionId || !connected) return

    const fetchData = async () => {
      setLoading(true)
      setAvailableLoading(true)
      try {
        const [agentRes, availRes] = await Promise.all([
          getSessionAgentProviders(sessionId),
          getAvailableProviders(sessionId),
        ])
        if (agentRes?.map) setAgentMap(agentRes.map)
        if (availRes?.providers) setAvailableProviders(availRes.providers)
      } catch {
        // best-effort
      }
      setLoading(false)
      setAvailableLoading(false)
    }

    fetchData()
  }, [sessionId, connected, setAvailableProviders, setAvailableLoading])

  const handleUpdateAgent = useCallback(
    async (agentName: string, namespace: string) => {
      if (!sessionId || updatingAgent) return
      setUpdatingAgent(agentName)
      try {
        // Build map with all current mappings + updated one
        const newMap: Record<string, string> = {}
        for (const [agent, detail] of Object.entries(agentMap)) {
          newMap[agent] = detail.namespace
        }
        if (namespace) {
          newMap[agentName] = namespace
        } else {
          delete newMap[agentName]
        }

        const res = await updateSessionAgentProviders(sessionId, { map: newMap })
        if (res?.map) {
          setAgentMap(res.map)
          toast.success(
            'Provider updated',
            `${agentName} assigned to ${namespace || 'default'}`
          )
        }
      } catch (e) {
        const err = e as Error
        toast.error('Update failed', err.message)
      }
      setUpdatingAgent(null)
    },
    [sessionId, agentMap, updatingAgent, toast]
  )

  if (!sessionId) {
    return (
      <div className="card p-6 text-center">
        <p className="text-sm text-text-muted">
          Connect to a session to configure per-agent providers.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="card p-4">
        <h3 className="text-sm font-display font-semibold text-neon-cyan tracking-wider mb-1">
          Per-Agent Provider Configuration
        </h3>
        <p className="text-[11px] text-text-muted">
          Assign different LLM providers to each agent. "Default" uses the resolution chain.
        </p>
      </div>

      <div className="card overflow-hidden">
        {/* Header */}
        <div className="px-4 py-2.5 border-b border-border-subtle bg-surface-elevated/50 flex items-center justify-between">
          <span className="text-[10px] font-mono text-text-dimmed uppercase tracking-wider">
            Agents ({agentRoster.length})
          </span>
          {loading && <Loader2 className="w-3.5 h-3.5 animate-spin text-text-muted" />}
        </div>

        {/* Rows */}
        <div className="divide-y divide-border-subtle">
          {agentRoster.map((item) => {
            const currentMapping = agentMap[item.agent]
            const currentNamespace = currentMapping?.namespace ?? ''
            const isUpdating = updatingAgent === item.agent

            return (
              <div
                key={item.agent}
                className="px-4 py-3 hover:bg-surface-elevated/30 transition-colors"
              >
                <div className="flex items-center justify-between gap-4">
                  {/* Agent icon + name */}
                  <div className="flex items-center gap-2 min-w-0 flex-1">
                    <div className="size-8 rounded-lg bg-neon-cyan/10 border border-neon-cyan/30 flex items-center justify-center flex-shrink-0">
                      <span className="text-xs font-bold text-neon-cyan uppercase">
                        {item.agent.slice(0, 2)}
                      </span>
                    </div>
                    <div className="min-w-0">
                      <div className="text-sm font-mono text-text-primary truncate">
                        {item.agent}
                      </div>
                      {currentMapping && (
                        <div className="text-[10px] font-mono text-text-dimmed truncate">
                          {currentMapping.name} / {currentMapping.model}
                        </div>
                      )}
                    </div>
                  </div>

                  {/* Selector */}
                  <div className="flex items-center gap-2 flex-shrink-0">
                    <ProviderSelector
                      value={currentNamespace}
                      onChange={(ns) => handleUpdateAgent(item.agent, ns)}
                      providers={availableProviders}
                      disabled={isUpdating || !connected}
                      className={cn('w-52', isUpdating && 'opacity-50')}
                    />
                    {isUpdating && (
                      <Loader2 className="w-4 h-4 animate-spin text-neon-cyan" />
                    )}
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}
