import React, { useState, useMemo, useRef, useEffect, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { useSisyphusStore, type InputMode } from '@/store/sisyphus-store'
import { sendMessage } from '@/lib/axiom-client'

// ============================================================
//  Composer - Enhanced message input with mode switch,
//  multi-agent selector, and file upload
// ============================================================

interface ComposerProps {
  sessionId: string | null
  connected: boolean
}

// Default agent roster if none from backend
const DEFAULT_AGENTS = [
  "research_assistant", "planner", "reasoner", 
  "librarian", "verifier", "dag_builder", "paper_editor"
]

const MODE_CONFIG = {
  chat: {
    label: "chat",
    color: "bg-neon-gold text-bg-base",
    hoverColor: "hover:bg-neon-gold/10 hover:text-neon-gold",
    placeholder: "Ask a scientific question...",
  },
  vibe: {
    label: "vibe",
    color: "bg-neon-cyan text-bg-base",
    hoverColor: "hover:bg-neon-cyan/10 hover:text-neon-cyan",
    placeholder: "Vibe researching... (goals + DAG + trace + multi-agent)",
  },
  vibe_loop: {
    label: "vibe_loop",
    color: "bg-neon-purple text-bg-base",
    hoverColor: "hover:bg-neon-purple/10 hover:text-neon-purple",
    placeholder: "Vibe loop... (auto multi-round until budget exhausted)",
  },
}

const Composer: React.FC<ComposerProps> = ({ sessionId, connected }) => {
  const { 
    inputMode, 
    setInputMode, 
    agentRoster, 
    isSending, 
    setIsSending,
    addMessage 
  } = useSisyphusStore()

  const [text, setText] = useState("")
  const [files, setFiles] = useState<File[]>([])
  const [toAgents, setToAgents] = useState<string[]>([])
  const [agentDropdownOpen, setAgentDropdownOpen] = useState(false)
  const agentDropdownRef = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  // Close dropdown on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (agentDropdownRef.current && !agentDropdownRef.current.contains(e.target as Node)) {
        setAgentDropdownOpen(false)
      }
    }
    document.addEventListener("mousedown", handleClickOutside)
    return () => document.removeEventListener("mousedown", handleClickOutside)
  }, [])

  // Derive agent list
  const agents = useMemo(() => {
    const fromRoster = agentRoster.map(r => r.agent).filter(Boolean)
    const list = fromRoster.length > 0 ? fromRoster : DEFAULT_AGENTS
    // Sort but keep research_assistant first
    return Array.from(new Set(list)).sort((a, b) => {
      if (a === "research_assistant") return -1
      if (b === "research_assistant") return 1
      return a.localeCompare(b)
    })
  }, [agentRoster])

  // Target label
  const targetLabel = useMemo(() => {
    const selected = toAgents.filter(Boolean)
    if (selected.includes("*")) return "All agents"
    if (selected.length === 0) return "research_assistant"
    if (selected.length === 1) return selected[0]
    return `${selected[0]} +${selected.length - 1}`
  }, [toAgents])

  const isBroadcast = toAgents.includes("*")

  // Toggle single agent
  const toggleAgent = (agent: string) => {
    if (isBroadcast) return
    setToAgents(prev => 
      prev.includes(agent) 
        ? prev.filter(a => a !== agent)
        : [...prev, agent]
    )
  }

  // Toggle broadcast
  const toggleBroadcast = () => {
    setToAgents(prev => prev.includes("*") ? [] : ["*"])
  }

  // Send message
  const handleSend = useCallback(async () => {
    const trimmed = text.trim()
    if (!sessionId || !connected || isSending || !trimmed) return

    setIsSending(true)

    // Add user message to store immediately
    addMessage({
      role: "user",
      content: trimmed,
    })

    // Set current run with user prompt (runId will be updated by RUN_STARTED event)
    const { setCurrentRun } = useSisyphusStore.getState()
    setCurrentRun(`pending-${Date.now()}`, trimmed)

    try {
      // Build payload
      const payload = {
        text: trimmed,
        mode: inputMode,
        toAgents: toAgents.length > 0 ? toAgents : undefined,
        // files: files.length > 0 ? files : undefined,  // TODO: file upload API
      }

      const result = await sendMessage(sessionId, payload)
      
      // Update runId from response if available
      if (result.ok && (result as { runId?: string }).runId) {
        setCurrentRun((result as { runId?: string }).runId!, trimmed)
      }
      
      setText("")
      setFiles([])
    } catch (e) {
      console.error("Failed to send message:", e)
    } finally {
      setIsSending(false)
    }
  }, [sessionId, connected, isSending, text, inputMode, toAgents, setIsSending, addMessage])

  // Handle enter key
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const config = MODE_CONFIG[inputMode]
  const disabled = !connected || !sessionId || isSending

  return (
    <div className="p-3 bg-surface/30 border-t border-border-subtle backdrop-blur-sm">
      {/* Unified Input Card */}
      <div className="rounded-xl border border-border-subtle bg-bg-surface/80 overflow-visible shadow-sm">
        {/* Input Row: textarea + send */}
        <div className="flex items-end gap-2 p-2.5">
          <input
            type="text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={
              !sessionId
                ? "Create/select a session first..."
                : !connected
                ? "Connecting..."
                : config.placeholder
            }
            disabled={disabled}
            className={cn(
              "flex-1 px-3 py-2 rounded-lg bg-transparent",
              "text-sm text-text-primary placeholder:text-text-dimmed",
              "border border-border-subtle focus:border-neon-cyan/40 outline-none",
              "transition-colors disabled:opacity-50"
            )}
          />
          <button
            onClick={handleSend}
            disabled={disabled || !text.trim()}
            aria-label="Send message"
            className={cn(
              "size-9 flex-shrink-0 rounded-lg flex items-center justify-center transition-all",
              text.trim() && !disabled
                ? "bg-neon-cyan text-bg-base shadow-glow-cyan hover:brightness-110"
                : "bg-surface-elevated text-text-dimmed border border-border-subtle",
              "disabled:opacity-40 disabled:shadow-none"
            )}
          >
            {isSending ? (
              <svg className="size-4 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
            ) : (
              <svg className="size-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
              </svg>
            )}
          </button>
        </div>

        {/* Controls Row: mode | agent | attach */}
        <div className="flex items-center gap-2 px-2.5 pb-2.5">
          {/* Mode Pills */}
          <div className="inline-flex rounded-lg bg-surface-elevated/50 p-0.5">
            {(["chat", "vibe", "vibe_loop"] as InputMode[]).map((mode) => {
              const modeConfig = MODE_CONFIG[mode]
              const isActive = inputMode === mode
              return (
                <button
                  key={mode}
                  onClick={() => setInputMode(mode)}
                  disabled={disabled}
                  className={cn(
                    "px-2.5 py-1 text-[10px] font-mono font-medium rounded-md transition-all",
                    isActive
                      ? modeConfig.color
                      : "text-text-muted hover:text-text-secondary",
                    "disabled:opacity-40"
                  )}
                >
                  {modeConfig.label}
                </button>
              )
            })}
          </div>

          {/* Divider */}
          <div className="w-px h-5 bg-border-subtle" />

          {/* Agent Selector */}
          <div className="relative" ref={agentDropdownRef}>
            <button
              onClick={() => setAgentDropdownOpen(!agentDropdownOpen)}
              disabled={disabled}
              className={cn(
                "flex items-center gap-1 px-2 py-1 rounded-md text-[11px] font-mono",
                "text-text-secondary hover:text-neon-cyan transition-colors",
                "disabled:opacity-40"
              )}
            >
              <svg className="size-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
              <span className="truncate max-w-[100px]">{targetLabel}</span>
              <svg className={cn("size-2.5 transition-transform", agentDropdownOpen && "rotate-180")} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
              </svg>
            </button>

            {/* Dropdown */}
            {agentDropdownOpen && (
              <div className="absolute left-0 bottom-full mb-2 z-50 w-52 max-h-[50vh] overflow-y-auto rounded-lg border border-border-subtle bg-bg-surface shadow-xl">
                <div className="px-3 py-1.5 text-[10px] text-text-dimmed border-b border-border-subtle">
                  Default: <span className="text-neon-cyan">research_assistant</span>
                </div>

                {/* Broadcast */}
                <label className="flex items-center gap-2 px-3 py-1.5 text-xs hover:bg-surface-elevated cursor-pointer">
                  <input type="checkbox" checked={isBroadcast} onChange={toggleBroadcast} className="accent-neon-cyan size-3" />
                  <span className="font-mono text-text-primary">* All (broadcast)</span>
                </label>

                <div className="border-t border-border-subtle" />

                {agents.map((agent) => (
                  <label key={agent} className="flex items-center gap-2 px-3 py-1.5 text-xs hover:bg-surface-elevated cursor-pointer">
                    <input
                      type="checkbox"
                      checked={toAgents.includes(agent)}
                      disabled={isBroadcast}
                      onChange={() => toggleAgent(agent)}
                      className="accent-neon-cyan size-3 disabled:opacity-50"
                    />
                    <span className={cn("font-mono text-[11px]", isBroadcast && "opacity-50")}>{agent}</span>
                  </label>
                ))}

                {toAgents.length > 0 && (
                  <>
                    <div className="border-t border-border-subtle" />
                    <button onClick={() => setToAgents([])} className="w-full text-left px-3 py-1.5 text-[10px] text-text-muted hover:bg-surface-elevated">
                      Clear
                    </button>
                  </>
                )}
              </div>
            )}
          </div>

          {/* Spacer */}
          <div className="flex-1" />

          {/* File indicator */}
          {files.length > 0 && (
            <div className="flex items-center gap-1 text-[10px] text-neon-gold font-mono">
              <svg className="size-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.172 7l-6.586 6.586a2 2 0 102.828 2.828l6.414-6.586a4 4 0 00-5.656-5.656l-6.415 6.585a6 6 0 108.486 8.486L20.5 13" />
              </svg>
              <span>{files.length}</span>
              <button onClick={() => setFiles([])} className="text-neon-red hover:brightness-125">×</button>
            </div>
          )}

          {/* Attach Button */}
          <input ref={fileInputRef} type="file" multiple className="hidden" onChange={(e) => setFiles(Array.from(e.target.files || []))} />
          <button
            onClick={() => fileInputRef.current?.click()}
            disabled={disabled}
            aria-label="Attach files"
            className={cn(
              "size-7 rounded-md flex items-center justify-center text-text-muted",
              "hover:text-neon-gold hover:bg-neon-gold/5 transition-colors",
              "disabled:opacity-40"
            )}
          >
            <svg className="size-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
            </svg>
          </button>
        </div>
      </div>

      {/* Footer */}
      <p className="text-center text-[9px] text-text-dimmed/60 font-mono mt-2 tracking-wide">
        Aevatar Framework · Claude Scientific Skills
      </p>
    </div>
  )
}

export default Composer
