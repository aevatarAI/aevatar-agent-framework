// ============================================================================
//  Settings Panel - Tools & MCP Tab
// ============================================================================

import React, { useEffect, useState, useMemo, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { getToolsSnapshot, reconnectMcp, syncSkills, getSkillsSyncStatus } from '@/lib/axiom-client'
import type { ToolSummary } from '@/types'
import SkillsMpModal from '../skills-mp-modal'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { StatBox } from './components'
import type { SkillsSyncStatus, SyncSkillsResult, SkillsSyncLog } from './types'

interface ToolsMcpTabProps {
  sessionId: string | null
  connected: boolean
  tools: ToolSummary[]
  setTools: (tools: ToolSummary[]) => void
}

export const ToolsMcpTab: React.FC<ToolsMcpTabProps> = ({
  sessionId,
  connected,
  tools,
  setTools,
}) => {
  const [mcpBusy, setMcpBusy] = useState(false)
  const [skillsBusy, setSkillsBusy] = useState(false)
  const [skillsNote, setSkillsNote] = useState('')
  const [skillsLogs, setSkillsLogs] = useState<SkillsSyncLog[]>([])
  const [skillsStatus, setSkillsStatus] = useState<SkillsSyncStatus | null>(null)
  const [error, setError] = useState('')
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<'all' | 'mcp' | 'skills'>('all')
  const [skillsMpOpen, setSkillsMpOpen] = useState(false)

  const mcpCount = useMemo(
    () => tools.filter((t) => (t.source || '').toUpperCase() === 'MCP').length,
    [tools]
  )
  const skillsCount = tools.length - mcpCount

  useEffect(() => {
    if (!sessionId || !connected) return
    const fetch = async () => {
      try {
        const res = await getToolsSnapshot(sessionId)
        setTools(res.tools || [])
      } catch {
        /* best-effort */
      }
    }
    fetch()
  }, [sessionId, connected, setTools])

  const filtered = useMemo(() => {
    let list = tools
    if (filter === 'mcp') list = list.filter((t) => (t.source || '').toUpperCase() === 'MCP')
    else if (filter === 'skills') list = list.filter((t) => (t.source || '').toUpperCase() !== 'MCP')
    const q = search.trim().toLowerCase()
    if (q)
      list = list.filter(
        (t) => t.name.toLowerCase().includes(q) || (t.description || '').toLowerCase().includes(q)
      )
    return list.slice(0, 200)
  }, [tools, search, filter])

  const handleReconnectMcp = useCallback(async () => {
    if (!sessionId || mcpBusy) return
    setMcpBusy(true)
    setError('')
    try {
      await reconnectMcp(sessionId)
      const res = await getToolsSnapshot(sessionId)
      setTools(res.tools || [])
      setSkillsNote('MCP reconnected successfully')
    } catch (e) {
      const err = e as Error
      setError(`MCP reconnect failed: ${err?.message || String(e)}`)
    } finally {
      setMcpBusy(false)
    }
  }, [sessionId, mcpBusy, setTools])

  const handleSyncSkills = useCallback(async () => {
    if (skillsBusy) return
    setSkillsBusy(true)
    setError('')
    setSkillsNote('')
    setSkillsLogs([])
    setSkillsStatus(null)

    let alive = true
    let timer: ReturnType<typeof setInterval> | null = null

    const poll = async () => {
      try {
        const st = (await getSkillsSyncStatus()) as SkillsSyncStatus
        if (!alive) return
        setSkillsStatus(st)
        setSkillsLogs((st?.logs || []).slice(-12))
        if (st?.running && st?.current?.repoUrl) {
          setSkillsNote(`Syncing: ${st.current.packName || ''} (${st.current.repoUrl})`)
        }
      } catch {
        /* best-effort */
      }
    }

    try {
      await poll()
      timer = setInterval(poll, 800)
      const res = (await syncSkills()) as SyncSkillsResult
      if (timer) clearInterval(timer)
      await poll()
      alive = false

      const packs = res?.packs || []
      const okCount = packs.filter((p) => p?.ok).length
      setSkillsNote(
        res?.ok ? `Skills updated: ${okCount}/${packs.length} packs` : `Partial: ${okCount}/${packs.length}`
      )

      if (sessionId) {
        const toolsRes = await getToolsSnapshot(sessionId)
        setTools(toolsRes.tools || [])
      }
    } catch (e) {
      const err = e as Error
      setError(`Skills sync failed: ${err?.message || String(e)}`)
    } finally {
      alive = false
      if (timer) clearInterval(timer)
      setSkillsBusy(false)
    }
  }, [skillsBusy, sessionId, setTools])

  return (
    <div className="space-y-6">
      {/* Stats + Actions */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Stats Card */}
        <div className="card p-5">
          <h3 className="text-sm font-display font-semibold text-neon-cyan tracking-wider mb-4">
            Tool Statistics
          </h3>
          <div className="grid grid-cols-3 gap-4">
            <StatBox label="Total" value={tools.length} />
            <StatBox label="MCP" value={mcpCount} accent="cyan" />
            <StatBox label="Skills" value={skillsCount} accent="gold" />
          </div>
        </div>

        {/* Actions Card */}
        <div className="card p-5">
          <h3 className="text-sm font-display font-semibold text-neon-cyan tracking-wider mb-4">
            Actions
          </h3>
          <div className="grid grid-cols-2 gap-3">
            <button
              onClick={handleReconnectMcp}
              disabled={!connected || mcpBusy}
              className="btn-secondary py-3 text-sm"
            >
              {mcpBusy ? 'Reconnecting…' : 'Reconnect MCP'}
            </button>
            <button
              onClick={handleSyncSkills}
              disabled={skillsBusy}
              className="btn-secondary py-3 text-sm flex items-center justify-center gap-2"
            >
              <svg
                className={cn('size-4', skillsBusy && 'animate-spin')}
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
                />
              </svg>
              {skillsBusy ? 'Updating…' : 'Update Skills'}
            </button>
          </div>
          {/* SkillsMP Marketplace Button */}
          <button
            onClick={() => setSkillsMpOpen(true)}
            className="w-full mt-3 py-3 text-sm rounded-lg border border-neon-gold/30 bg-neon-gold/5 text-neon-gold hover:bg-neon-gold/10 transition-colors flex items-center justify-center gap-2"
          >
            <svg className="size-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
                d="M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z"
              />
            </svg>
            SkillsMP Marketplace
          </button>
          {skillsNote && <p className="mt-3 text-xs font-mono text-text-muted">{skillsNote}</p>}
          {error && <p className="mt-3 text-xs font-mono text-neon-red">{error}</p>}
        </div>
      </div>

      {/* SkillsMP Modal */}
      <SkillsMpModal
        open={skillsMpOpen}
        onClose={() => setSkillsMpOpen(false)}
        onInstalled={async () => {
          if (sessionId) {
            const res = await getToolsSnapshot(sessionId)
            setTools(res.tools || [])
          }
        }}
      />

      {/* Sync Logs */}
      {(skillsBusy || skillsLogs.length > 0) && (
        <div className="card p-4">
          <h4 className="text-xs font-mono text-text-dimmed mb-2">Sync Logs</h4>
          <div className="max-h-32 overflow-auto rounded border border-border-subtle bg-bg-void p-2 font-mono text-[10px] text-text-muted">
            {skillsBusy && (
              <div className="text-neon-cyan mb-1">{skillsStatus?.current?.step || 'initializing…'}</div>
            )}
            {skillsLogs.map((l, i) => (
              <div key={i} className="whitespace-pre-wrap">
                {l?.message || ''}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Tools List */}
      <div className="card overflow-hidden">
        <div className="p-4 border-b border-border-subtle flex flex-col sm:flex-row gap-3">
          <div className="relative flex-1">
            <svg
              className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-text-dimmed"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
              />
            </svg>
            <input
              type="text"
              placeholder="Search tools..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="input w-full pl-10"
            />
          </div>
          <Select value={filter} onValueChange={(v) => setFilter(v as 'all' | 'mcp' | 'skills')}>
            <SelectTrigger className="w-full sm:w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All ({tools.length})</SelectItem>
              <SelectItem value="mcp">MCP ({mcpCount})</SelectItem>
              <SelectItem value="skills">Skills ({skillsCount})</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="max-h-[50vh] overflow-auto divide-y divide-border-subtle">
          {filtered.length === 0 ? (
            <div className="p-8 text-center text-sm text-text-muted">
              {tools.length === 0
                ? 'No tools available. Try "Reconnect MCP" or "Update Skills".'
                : 'No tools match your search.'}
            </div>
          ) : (
            filtered.map((tool, idx) => (
              <div
                key={`${tool.name}-${idx}`}
                className="px-4 py-3 hover:bg-surface-elevated transition-colors"
              >
                <div className="flex items-center gap-2">
                  <span className="font-mono text-sm text-text-primary truncate flex-1">
                    {tool.name}
                  </span>
                  {(tool.source || '').toUpperCase() === 'MCP' ? (
                    <span className="text-[9px] font-mono text-neon-cyan bg-neon-cyan/10 px-1.5 py-0.5 rounded">
                      MCP
                    </span>
                  ) : (
                    tool.source && (
                      <span className="text-[9px] font-mono text-text-dimmed bg-surface-accent px-1.5 py-0.5 rounded">
                        {tool.source}
                      </span>
                    )
                  )}
                </div>
                {tool.description && (
                  <div className="text-xs text-text-muted mt-1 line-clamp-1">{tool.description}</div>
                )}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  )
}
