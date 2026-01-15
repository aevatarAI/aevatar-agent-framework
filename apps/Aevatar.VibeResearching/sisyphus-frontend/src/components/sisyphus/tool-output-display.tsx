import React, { useState, useMemo } from 'react'
import { cn } from '@/lib/utils'
import type { ToolOutput } from '@/types'

// ============================================================
//  Tool Output Display Component
//  Renders tool call results with status, MCP badge, and output
// ============================================================

interface ToolOutputDisplayProps {
  tools: ToolOutput[]
  className?: string
}

/**
 * Format tool payload for display
 * Extracts value from Aevatar tool result shape or shows pretty JSON
 */
function formatToolPayload(raw: string): { main: string; rawJson?: string } {
  const s = (raw ?? '').trim()
  if (!s) return { main: '' }

  try {
    const obj = JSON.parse(s) as { data?: { value?: string }; success?: boolean }
    const value = obj?.data?.value
    if (typeof value === 'string' && value.length > 0) {
      return { main: value, rawJson: s }
    }
    const pretty = JSON.stringify(obj, null, 2)
    return { main: pretty, rawJson: s }
  } catch {
    return { main: s }
  }
}

const ToolOutputDisplay: React.FC<ToolOutputDisplayProps> = ({ tools, className }) => {
  const [isOpen, setIsOpen] = useState(false)
  const [userToggled, setUserToggled] = useState(false)

  const toolsRunning = useMemo(() => tools.some(t => t.status === 'running'), [tools])

  // Auto-open when tools start running
  React.useEffect(() => {
    if (!userToggled && toolsRunning) {
      setIsOpen(true)
    }
  }, [toolsRunning, userToggled])

  if (tools.length === 0) return null

  const handleToggle = (e: React.MouseEvent) => {
    e.preventDefault()
    setUserToggled(true)
    setIsOpen(!isOpen)
  }

  return (
    <div className={cn("mt-4 rounded-lg border border-border-subtle overflow-hidden", className)}>
      {/* Header / Summary */}
      <button
        onClick={handleToggle}
        className="w-full flex items-center justify-between px-3 py-2 bg-bg-elevated hover:bg-bg-accent transition-colors text-left"
      >
        <div className="flex items-center gap-2">
          <svg className="size-4 text-neon-purple" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M11.42 15.17L17.25 21A2.652 2.652 0 0021 17.25l-5.877-5.877M11.42 15.17l2.496-3.03c.317-.384.74-.626 1.208-.766M11.42 15.17l-4.655 5.653a2.548 2.548 0 11-3.586-3.586l6.837-5.63m5.108-.233c.55-.164 1.163-.188 1.743-.14a4.5 4.5 0 004.486-6.336l-3.276 3.277a3.004 3.004 0 01-2.25-2.25l3.276-3.276a4.5 4.5 0 00-6.336 4.486c.091 1.076-.071 2.264-.904 2.95l-.102.085m-1.745 1.437L5.909 7.5H4.5L2.25 3.75l1.5-1.5L7.5 4.5v1.409l4.26 4.26m-1.745 1.437l1.745-1.437m6.615 8.206L15.75 15.75M4.867 19.125h.008v.008h-.008v-.008z" />
          </svg>
          <span className="text-xs font-mono text-text-secondary">Tools</span>
          <span className="text-[10px] font-mono text-text-dimmed">({tools.length})</span>
          {toolsRunning && (
            <span className="text-[9px] px-1.5 py-0.5 rounded bg-neon-cyan/10 text-neon-cyan border border-neon-cyan/30 animate-pulse">
              RUNNING
            </span>
          )}
        </div>
        <svg
          className={cn("size-4 text-text-muted transition-transform", isOpen && "rotate-180")}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {/* Expanded Content */}
      {isOpen && (
        <div className="p-3 space-y-2 bg-bg-surface">
          {tools.map((tool) => (
            <ToolItem key={tool.toolCallId} tool={tool} />
          ))}
        </div>
      )}
    </div>
  )
}

// ============================================================
//  Individual Tool Item
// ============================================================

interface ToolItemProps {
  tool: ToolOutput
}

const ToolItem: React.FC<ToolItemProps> = ({ tool }) => {
  const [showRaw, setShowRaw] = useState(false)
  const formatted = useMemo(() => formatToolPayload(tool.resultPreview || ''), [tool.resultPreview])

  return (
    <div className="rounded-lg border border-border-subtle bg-bg-base p-3">
      {/* Tool Header */}
      <div className="flex items-center gap-2 flex-wrap">
        <svg className="size-3.5 text-neon-purple" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11.42 15.17L17.25 21A2.652 2.652 0 0021 17.25l-5.877-5.877M11.42 15.17l2.496-3.03c.317-.384.74-.626 1.208-.766M11.42 15.17l-4.655 5.653a2.548 2.548 0 11-3.586-3.586l6.837-5.63m5.108-.233c.55-.164 1.163-.188 1.743-.14a4.5 4.5 0 004.486-6.336l-3.276 3.277a3.004 3.004 0 01-2.25-2.25l3.276-3.276a4.5 4.5 0 00-6.336 4.486c.091 1.076-.071 2.264-.904 2.95l-.102.085m-1.745 1.437L5.909 7.5H4.5L2.25 3.75l1.5-1.5L7.5 4.5v1.409l4.26 4.26m-1.745 1.437l1.745-1.437m6.615 8.206L15.75 15.75M4.867 19.125h.008v.008h-.008v-.008z" />
        </svg>
        <span className="text-xs font-mono text-neon-purple">{tool.name}</span>

        {/* MCP Badge */}
        {tool.isMcp && (
          <span className="text-[9px] px-1.5 py-0.5 rounded bg-neon-cyan/10 text-neon-cyan border border-neon-cyan/30">
            MCP
          </span>
        )}

        {/* Status Badge */}
        {tool.status === 'running' && (
          <span className="text-[9px] px-1.5 py-0.5 rounded bg-bg-elevated text-text-muted border border-border-subtle animate-pulse">
            RUNNING
          </span>
        )}
        {tool.status === 'done' && tool.success === false && (
          <span className="text-[9px] px-1.5 py-0.5 rounded bg-neon-red/10 text-neon-red border border-neon-red/30">
            FAIL
          </span>
        )}
        {tool.status === 'done' && tool.success !== false && (
          <span className="text-[9px] px-1.5 py-0.5 rounded bg-neon-green/10 text-neon-green border border-neon-green/30">
            OK
          </span>
        )}

        {/* Duration */}
        {tool.durationMs != null && tool.status === 'done' && (
          <span className="text-[9px] font-mono text-text-dimmed tabular-nums">
            {tool.durationMs}ms
          </span>
        )}
      </div>

      {/* Tool Output */}
      <div className="mt-2">
        {tool.error ? (
          <pre className="text-xs font-mono text-neon-red whitespace-pre-wrap break-words max-h-[200px] overflow-auto">
            {tool.error}
          </pre>
        ) : formatted.main ? (
          <div className="space-y-2">
            <pre className="text-xs font-mono text-text-primary whitespace-pre-wrap break-words max-h-[200px] overflow-auto p-2 rounded bg-bg-elevated border border-border-subtle">
              {formatted.main}
            </pre>
            {formatted.rawJson && (
              <details
                open={showRaw}
                onToggle={(e) => setShowRaw(e.currentTarget.open)}
                className="text-text-muted"
              >
                <summary className="text-[10px] font-mono cursor-pointer hover:text-neon-cyan transition-colors">
                  Raw JSON
                </summary>
                <pre className="mt-2 text-[10px] font-mono text-text-dimmed whitespace-pre-wrap break-words max-h-[150px] overflow-auto p-2 rounded bg-bg-void border border-border-subtle">
                  {formatted.rawJson}
                </pre>
              </details>
            )}
          </div>
        ) : tool.status === 'running' ? (
          <div className="flex items-center gap-2 py-2">
            <div className="size-2 rounded-full bg-neon-cyan animate-pulse" />
            <span className="text-[10px] font-mono text-text-muted">Executing...</span>
          </div>
        ) : (
          <span className="text-[10px] font-mono text-text-dimmed italic">no output</span>
        )}
      </div>
    </div>
  )
}

export default ToolOutputDisplay
