import React, { useState, useEffect, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog'
import {
  getSkillsMpStatus,
  searchSkillsMp,
  installSkillPack,
  setSecret,
  type SkillsMpItem,
  type SkillsMpStatus,
} from '@/lib/axiom-client'

// ============================================================
//  SkillsMP Marketplace Modal
//  Search, browse, and install skill packs from the marketplace
// ============================================================

interface SkillsMpModalProps {
  open: boolean
  onClose: () => void
  onInstalled?: () => void
}

const SkillsMpModal: React.FC<SkillsMpModalProps> = ({ open, onClose, onInstalled }) => {
  // === Auth State ===
  const [status, setStatus] = useState<SkillsMpStatus | null>(null)
  const [apiKeyInput, setApiKeyInput] = useState('')

  // === Search State ===
  const [query, setQuery] = useState('')
  const [searchMode, setSearchMode] = useState<'search' | 'ai-search'>('search')
  const [searchResults, setSearchResults] = useState<SkillsMpItem[]>([])

  // === Install State ===
  const [packName, setPackName] = useState('')
  const [repoUrl, setRepoUrl] = useState('')
  const [gitRef, setGitRef] = useState('main')
  const [skillsSubDir, setSkillsSubDir] = useState('skills')
  const [syncNow, setSyncNow] = useState(true)

  // === UI State ===
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [note, setNote] = useState('')

  // === Refresh Status ===
  const refreshStatus = useCallback(async () => {
    try {
      const res = await getSkillsMpStatus()
      setStatus(res)
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(`Failed to get SkillsMP status: ${msg}`)
    }
  }, [])

  useEffect(() => {
    if (!open) return
    setError('')
    setNote('')
    setSearchResults([])
    void refreshStatus()
  }, [open, refreshStatus])

  // === Save API Key ===
  const handleSaveApiKey = useCallback(async () => {
    const key = apiKeyInput.trim()
    if (!key || busy) return

    setBusy(true)
    setError('')
    setNote('')
    try {
      await setSecret('SkillsMP:ApiKey', key)
      setApiKeyInput('')
      await refreshStatus()
      setNote('SkillsMP API key saved successfully')
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(`Save failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }, [apiKeyInput, busy, refreshStatus])

  // === Search ===
  const handleSearch = useCallback(async () => {
    const q = query.trim()
    if (!q || busy) return

    setBusy(true)
    setError('')
    setNote('')
    setSearchResults([])
    try {
      const res = await searchSkillsMp(q, searchMode, { limit: 20, sortBy: 'stars' })
      if (res.ok) {
        setSearchResults(res.items || [])
        setNote(`Found ${res.items?.length || 0} skill pack(s)`)
      } else {
        setError('Search returned no results')
      }
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(`Search failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }, [query, searchMode, busy])

  // === Install ===
  const handleInstall = useCallback(async () => {
    const url = repoUrl.trim()
    if (!url || busy) return

    setBusy(true)
    setError('')
    setNote('')
    try {
      const res = await installSkillPack({
        name: packName.trim() || undefined,
        repoUrl: url,
        ref: gitRef.trim() || 'main',
        skillsSubDir: skillsSubDir.trim() || 'skills',
        sync: syncNow,
      }) as { ok?: boolean; error?: string }

      if (res?.ok) {
        setNote(syncNow ? 'Skill pack installed and synced!' : 'Skill pack config saved. Click "Update Skills" to sync.')
        onInstalled?.()
      } else {
        setError(res?.error || 'Install failed')
      }
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(`Install failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }, [repoUrl, packName, gitRef, skillsSubDir, syncNow, busy, onInstalled])

  // === Pick Item ===
  const handlePickItem = useCallback((item: SkillsMpItem) => {
    if (item.name) setPackName(item.name)
    if (item.repoUrl) setRepoUrl(item.repoUrl)
  }, [])

  // === Key handler for search ===
  const handleSearchKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !busy) {
      handleSearch()
    }
  }, [busy, handleSearch])

  if (!open) return null

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-w-4xl bg-bg-surface/95 backdrop-blur-md">
        <DialogHeader className="p-5 border-b border-border-subtle">
          <div className="flex items-center gap-3">
            <div className="size-10 rounded-lg bg-neon-gold/20 flex items-center justify-center">
              <svg className="size-5 text-neon-gold" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z" />
              </svg>
            </div>
            <div>
              <DialogTitle className="text-neon-gold">SkillsMP Marketplace</DialogTitle>
              <DialogDescription>Search and install agent skill packs</DialogDescription>
            </div>
          </div>
          <DialogCloseButton />
        </DialogHeader>

        <div className="max-h-[70vh] overflow-y-auto">
          {/* Auth Section */}
          <div className="p-4 border-b border-border-subtle">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs font-mono text-text-muted uppercase tracking-wider">Authentication</span>
              <span className={cn(
                "text-[10px] font-mono px-2 py-0.5 rounded",
                status?.configured
                  ? "bg-neon-green/10 text-neon-green border border-neon-green/30"
                  : "bg-neon-red/10 text-neon-red border border-neon-red/30"
              )}>
                {status?.configured ? `✓ Configured (${status.masked})` : '○ Not configured'}
              </span>
            </div>

            {!status?.configured && (
              <div className="flex gap-2">
                <input
                  type="password"
                  value={apiKeyInput}
                  onChange={(e) => setApiKeyInput(e.target.value)}
                  placeholder="Paste SkillsMP API key..."
                  className="input flex-1 font-mono text-sm"
                />
                <button
                  onClick={handleSaveApiKey}
                  disabled={busy || !apiKeyInput.trim()}
                  className="btn-primary px-4 text-sm"
                >
                  Save
                </button>
              </div>
            )}
          </div>

          {/* Search Section */}
          <div className="p-4 border-b border-border-subtle">
            <span className="text-xs font-mono text-text-muted uppercase tracking-wider block mb-3">Search Skills</span>
            <div className="flex gap-2">
              <div className="relative flex-1">
                <svg className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-text-dimmed" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                <input
                  type="text"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  onKeyDown={handleSearchKeyDown}
                  placeholder="Search skills (e.g. web scraping, bioinformatics)..."
                  className="input pl-10 text-sm"
                />
              </div>
              <select
                value={searchMode}
                onChange={(e) => setSearchMode(e.target.value as 'search' | 'ai-search')}
                className="input w-32 text-sm"
              >
                <option value="search">Keyword</option>
                <option value="ai-search">AI Search</option>
              </select>
              <button
                onClick={handleSearch}
                disabled={busy || !query.trim()}
                className="btn-secondary px-4 text-sm"
              >
                {busy ? 'Searching...' : 'Search'}
              </button>
            </div>
          </div>

          {/* Messages */}
          {(error || note) && (
            <div className="px-4 py-2 border-b border-border-subtle">
              {note && <p className="text-xs font-mono text-neon-green">{note}</p>}
              {error && <p className="text-xs font-mono text-neon-red">{error}</p>}
            </div>
          )}

          {/* Main Content: Results + Install */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-0">
            {/* Search Results */}
            <div className="p-4 border-r border-border-subtle">
              <span className="text-xs font-mono text-text-muted uppercase tracking-wider block mb-3">
                Results ({searchResults.length})
              </span>
              <div className="space-y-2 max-h-[40vh] overflow-y-auto pr-2">
                {searchResults.length === 0 ? (
                  <div className="text-sm text-text-muted text-center py-8">
                    No results yet. Run a search to find skill packs.
                  </div>
                ) : (
                  searchResults.map((item, idx) => (
                    <SkillItem key={item.id || idx} item={item} onPick={handlePickItem} />
                  ))
                )}
              </div>
            </div>

            {/* Install Form */}
            <div className="p-4">
              <span className="text-xs font-mono text-text-muted uppercase tracking-wider block mb-3">Install from Git</span>
              <p className="text-[10px] text-text-dimmed mb-4 leading-relaxed">
                Add a skill pack by specifying its Git repository. The pack will be cloned and synced to your workspace.
              </p>

              <div className="space-y-3">
                {/* Repo URL */}
                <div>
                  <label className="text-[10px] font-mono text-text-muted block mb-1">Repository URL *</label>
                  <input
                    type="text"
                    value={repoUrl}
                    onChange={(e) => setRepoUrl(e.target.value)}
                    placeholder="https://github.com/org/repo.git"
                    className="input text-sm font-mono"
                  />
                </div>

                {/* Pack Name + Ref */}
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="text-[10px] font-mono text-text-muted block mb-1">Pack Name</label>
                    <input
                      type="text"
                      value={packName}
                      onChange={(e) => setPackName(e.target.value)}
                      placeholder="(optional)"
                      className="input text-sm"
                    />
                  </div>
                  <div>
                    <label className="text-[10px] font-mono text-text-muted block mb-1">Git Ref</label>
                    <input
                      type="text"
                      value={gitRef}
                      onChange={(e) => setGitRef(e.target.value)}
                      placeholder="main"
                      className="input text-sm font-mono"
                    />
                  </div>
                </div>

                {/* Skills SubDir */}
                <div>
                  <label className="text-[10px] font-mono text-text-muted block mb-1">Skills SubDir</label>
                  <input
                    type="text"
                    value={skillsSubDir}
                    onChange={(e) => setSkillsSubDir(e.target.value)}
                    placeholder="skills"
                    className="input text-sm font-mono"
                  />
                  <p className="text-[9px] text-text-dimmed mt-1">
                    Directory containing skill definitions (e.g. "skills" or "scientific-skills")
                  </p>
                </div>

                {/* Sync Option */}
                <label className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={syncNow}
                    onChange={(e) => setSyncNow(e.target.checked)}
                    className="size-4 rounded border-border-default bg-bg-base accent-neon-cyan"
                  />
                  <span className="text-xs text-text-secondary">Sync immediately after saving</span>
                </label>

                {/* Install Button */}
                <button
                  onClick={handleInstall}
                  disabled={busy || !repoUrl.trim()}
                  className="w-full btn-primary py-3 text-sm mt-2"
                >
                  {busy ? 'Installing...' : 'Install Skill Pack'}
                </button>
              </div>
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Skill Item Component
// ============================================================

interface SkillItemProps {
  item: SkillsMpItem
  onPick: (item: SkillsMpItem) => void
}

const SkillItem: React.FC<SkillItemProps> = ({ item, onPick }) => {
  return (
    <div className="card p-3 hover:border-neon-gold/30 transition-colors">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <h4 className="text-sm font-semibold text-text-primary truncate">
            {item.name || '(unnamed)'}
          </h4>
          {item.description && (
            <p className="text-[11px] text-text-muted mt-1 line-clamp-2">{item.description}</p>
          )}
          <div className="flex items-center gap-3 mt-2">
            {item.repoUrl && (
              <span className="text-[9px] font-mono text-text-dimmed truncate max-w-[200px]">
                {item.repoUrl}
              </span>
            )}
            {typeof item.stars === 'number' && (
              <span className="text-[9px] font-mono text-neon-gold flex items-center gap-1">
                <svg className="size-3" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z" />
                </svg>
                {item.stars}
              </span>
            )}
          </div>
        </div>
        <div className="flex flex-col gap-1.5 shrink-0">
          {item.url && (
            <a
              href={item.url}
              target="_blank"
              rel="noreferrer"
              className="p-1.5 rounded border border-border-subtle hover:border-neon-cyan/30 hover:bg-neon-cyan/5 transition-colors"
              aria-label="Open in browser"
            >
              <svg className="size-3.5 text-text-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
              </svg>
            </a>
          )}
          <button
            onClick={() => onPick(item)}
            className="text-[9px] font-mono px-2 py-1.5 rounded bg-neon-gold/10 text-neon-gold border border-neon-gold/30 hover:bg-neon-gold/20 transition-colors"
          >
            Pick
          </button>
        </div>
      </div>
    </div>
  )
}

export default SkillsMpModal
