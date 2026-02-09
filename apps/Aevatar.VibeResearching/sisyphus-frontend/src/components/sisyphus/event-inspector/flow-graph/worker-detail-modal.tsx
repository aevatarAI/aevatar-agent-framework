// ============================================================
//  Worker Detail Modal - Shows full Worker proposal details
//  Displays system prompt, user prompt, assistant response
//  and related event history list
// ============================================================

import React, { useState, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import { X, User, Star, FileText, MessageSquare, Bot, Clock, Zap, List, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { ClassifiedEvent } from '@/types'

export interface WorkerDetail {
  id: string
  name: string
  color: string
  isLeader: boolean
  status: 'idle' | 'running' | 'completed'
  proposalId?: string
  summary?: string
  systemPrompt?: string
  userPrompt?: string
  assistantResponse?: string
  tokensUsed?: number
  timestamp?: number
  workerIndex?: number  // Index for filtering events
}

interface WorkerDetailModalProps {
  worker: WorkerDetail | null
  isOpen: boolean
  onClose: () => void
  events?: ClassifiedEvent[]  // All events for filtering
}

const WorkerDetailModal: React.FC<WorkerDetailModalProps> = ({
  worker,
  isOpen,
  onClose,
  events = [],
}) => {
  const [activeTab, setActiveTab] = useState<'proposal' | 'events'>('proposal')

  // Filter events related to this worker
  const workerEvents = useMemo(() => {
    if (!worker || worker.workerIndex === undefined) return []
    
    return events.filter(event => {
      const workerId = event.raw.fields.worker_id as string | undefined
      if (!workerId) return false
      
      // Match worker index from worker_id (e.g., "worker-0", "worker_1")
      const match = workerId.match(/worker[-_]?(\d+)/i)
      if (match) {
        const eventWorkerIndex = parseInt(match[1], 10)
        return eventWorkerIndex === worker.workerIndex
      }
      return false
    }).sort((a, b) => {
      // Sort by timestamp descending (newest first)
      const timeA = a.raw.timestamp ? new Date(a.raw.timestamp).getTime() : 0
      const timeB = b.raw.timestamp ? new Date(b.raw.timestamp).getTime() : 0
      return timeB - timeA
    })
  }, [events, worker])

  if (!isOpen || !worker) return null

  const modalContent = (
    <AnimatePresence>
      {isOpen && (
        <div className="fixed inset-0 z-[200] flex items-center justify-center">
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.15 }}
            className="absolute inset-0 bg-black/60 backdrop-blur-sm"
            onClick={onClose}
          />

          {/* Modal */}
          <motion.div
            initial={{ opacity: 0, scale: 0.95, y: 20 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.95, y: 20 }}
            transition={{ type: 'spring', damping: 25, stiffness: 300 }}
            className="relative z-10 w-[90%] max-w-2xl max-h-[80vh] overflow-hidden
                       bg-bg-surface border border-border-subtle rounded-xl shadow-2xl"
          >
            {/* Header */}
            <div
              className="flex items-center justify-between px-5 py-4 border-b border-border-subtle"
              style={{ background: `linear-gradient(135deg, ${worker.color}15, transparent)` }}
            >
              <div className="flex items-center gap-3">
                <div
                  className="w-10 h-10 rounded-lg flex items-center justify-center border-2"
                  style={{ borderColor: worker.color, backgroundColor: `${worker.color}20` }}
                >
                  <User className="w-5 h-5" style={{ color: worker.color }} />
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <h3
                      className="text-sm font-mono font-semibold"
                      style={{ color: worker.color }}
                    >
                      {worker.name.toUpperCase()}
                    </h3>
                    {worker.isLeader && (
                      <span className="flex items-center gap-1 px-1.5 py-0.5 rounded bg-neon-green/20 text-neon-green text-[9px] font-mono">
                        <Star className="w-2.5 h-2.5" fill="currentColor" />
                        LEADER
                      </span>
                    )}
                  </div>
                  <div className="flex items-center gap-3 text-[10px] text-text-muted font-mono mt-0.5">
                    {worker.proposalId && (
                      <span className="flex items-center gap-1">
                        <FileText className="w-3 h-3" />
                        {worker.proposalId}
                      </span>
                    )}
                    {worker.tokensUsed && (
                      <span className="flex items-center gap-1">
                        <Zap className="w-3 h-3" />
                        {worker.tokensUsed} tokens
                      </span>
                    )}
                  </div>
                </div>
              </div>
              <button
                onClick={onClose}
                className="p-2 rounded-lg text-text-muted hover:text-text-primary hover:bg-bg-elevated transition-colors"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Tab Navigation */}
            <div className="flex border-b border-border-subtle">
              <button
                onClick={() => setActiveTab('proposal')}
                className={cn(
                  "flex items-center gap-2 px-4 py-2.5 text-xs font-mono transition-colors border-b-2 -mb-px",
                  activeTab === 'proposal'
                    ? "text-neon-cyan border-neon-cyan"
                    : "text-text-muted border-transparent hover:text-text-secondary"
                )}
              >
                <FileText className="w-3.5 h-3.5" />
                Proposal
              </button>
              <button
                onClick={() => setActiveTab('events')}
                className={cn(
                  "flex items-center gap-2 px-4 py-2.5 text-xs font-mono transition-colors border-b-2 -mb-px",
                  activeTab === 'events'
                    ? "text-neon-cyan border-neon-cyan"
                    : "text-text-muted border-transparent hover:text-text-secondary"
                )}
              >
                <List className="w-3.5 h-3.5" />
                Events
                {workerEvents.length > 0 && (
                  <span className="px-1.5 py-0.5 rounded bg-neon-cyan/20 text-neon-cyan text-[9px]">
                    {workerEvents.length}
                  </span>
                )}
              </button>
            </div>

            {/* Content */}
            <div className="p-5 overflow-y-auto max-h-[calc(80vh-130px)] space-y-4">
              {/* Proposal Tab */}
              {activeTab === 'proposal' && (
                <>
                  {/* Status indicator */}
                  <div className="flex items-center gap-2 text-xs font-mono">
                    <div className={cn(
                      "w-2 h-2 rounded-full",
                      worker.status === 'completed' ? "bg-neon-green" :
                        worker.status === 'running' ? "bg-neon-cyan animate-pulse" :
                          "bg-text-muted"
                    )} />
                    <span className="text-text-muted capitalize">{worker.status}</span>
                    {worker.timestamp && (
                      <span className="flex items-center gap-1 ml-auto text-text-muted">
                        <Clock className="w-3 h-3" />
                        {new Date(worker.timestamp).toLocaleTimeString()}
                      </span>
                    )}
                  </div>

                  {/* System Prompt */}
                  {worker.systemPrompt && (
                    <ConversationBlock
                      type="system"
                      label="System Prompt"
                      content={worker.systemPrompt}
                    />
                  )}

                  {/* User Prompt */}
                  {worker.userPrompt && (
                    <ConversationBlock
                      type="user"
                      label="User Prompt"
                      content={worker.userPrompt}
                    />
                  )}

                  {/* Assistant Response */}
                  {worker.assistantResponse && (
                    <ConversationBlock
                      type="assistant"
                      label="Proposal Response"
                      content={worker.assistantResponse}
                      color={worker.color}
                    />
                  )}

                  {/* Empty state */}
                  {!worker.systemPrompt && !worker.userPrompt && !worker.assistantResponse && (
                    <div className="text-center py-8 text-text-muted">
                      <MessageSquare className="w-8 h-8 mx-auto mb-2 opacity-50" />
                      <p className="text-sm font-mono">No conversation data available</p>
                      <p className="text-xs mt-1 opacity-70">
                        Waiting for Worker to submit proposal...
                      </p>
                    </div>
                  )}
                </>
              )}

              {/* Events Tab */}
              {activeTab === 'events' && (
                <>
                  {workerEvents.length > 0 ? (
                    <div className="space-y-2">
                      {workerEvents.map((event, index) => (
                        <EventListItem
                          key={event.id || index}
                          event={event}
                          color={worker.color}
                        />
                      ))}
                    </div>
                  ) : (
                    <div className="text-center py-8 text-text-muted">
                      <List className="w-8 h-8 mx-auto mb-2 opacity-50" />
                      <p className="text-sm font-mono">No events found</p>
                      <p className="text-xs mt-1 opacity-70">
                        Events related to this worker will appear here
                      </p>
                    </div>
                  )}
                </>
              )}
            </div>
          </motion.div>
        </div>
      )}
    </AnimatePresence>
  )

  return createPortal(modalContent, document.body)
}

// ============================================================
//  Event List Item Component
// ============================================================

interface EventListItemProps {
  event: ClassifiedEvent
  color: string
}

const EventListItem: React.FC<EventListItemProps> = ({ event, color }) => {
  const [isExpanded, setIsExpanded] = useState(false)

  // Category badge colors
  const categoryColors: Record<string, { bg: string; text: string }> = {
    proposal: { bg: 'bg-neon-green/20', text: 'text-neon-green' },
    vote: { bg: 'bg-neon-cyan/20', text: 'text-neon-cyan' },
    consensus: { bg: 'bg-yellow-500/20', text: 'text-yellow-400' },
    tool_call: { bg: 'bg-purple-500/20', text: 'text-purple-400' },
    thinking: { bg: 'bg-slate-500/20', text: 'text-slate-400' },
    other: { bg: 'bg-slate-500/20', text: 'text-slate-400' },
  }

  const catStyle = categoryColors[event.category] || categoryColors.other
  const timestamp = event.timestamp ? new Date(event.timestamp).toLocaleTimeString() : ''
  const eventName = event.raw.phase || event.category

  // Get preview content
  const getPreview = () => {
    // Try message first
    if (event.message) {
      const content = event.message
      return content.length > 100 ? content.slice(0, 100) + '...' : content
    }
    // Try assistant_response from fields
    if (event.raw.fields.assistant_response) {
      const content = event.raw.fields.assistant_response
      return content.length > 100 ? content.slice(0, 100) + '...' : content
    }
    return null
  }

  const preview = getPreview()

  return (
    <div
      className={cn(
        "rounded-lg border transition-all cursor-pointer",
        isExpanded ? "bg-bg-elevated" : "hover:bg-bg-elevated/50"
      )}
      style={{ borderColor: `${color}30` }}
      onClick={() => setIsExpanded(!isExpanded)}
    >
      {/* Header */}
      <div className="flex items-center gap-2 px-3 py-2">
        <ChevronRight
          className={cn(
            "w-3.5 h-3.5 text-text-muted transition-transform",
            isExpanded && "rotate-90"
          )}
        />
        <span className={cn("px-1.5 py-0.5 rounded text-[9px] font-mono uppercase", catStyle.bg, catStyle.text)}>
          {event.category}
        </span>
        <span className="text-xs text-text-secondary font-mono truncate flex-1">
          {eventName}
        </span>
        <span className="text-[10px] text-text-muted font-mono">
          {timestamp}
        </span>
      </div>

      {/* Preview (when collapsed) */}
      {!isExpanded && preview && (
        <div className="px-3 pb-2 pl-8">
          <p className="text-[10px] text-text-muted font-mono truncate">
            {preview}
          </p>
        </div>
      )}

      {/* Expanded Content */}
      {isExpanded && (
        <div className="px-3 pb-3 pl-8 space-y-2">
          {/* Event ID */}
          <div className="flex items-center gap-2 text-[10px] font-mono">
            <span className="text-text-muted">ID:</span>
            <span className="text-text-secondary">{event.id}</span>
          </div>

          {/* Fields */}
          <div className="rounded bg-bg-surface/50 p-2 max-h-48 overflow-y-auto">
            <pre className="text-[10px] text-text-secondary font-mono whitespace-pre-wrap">
              {JSON.stringify(event.raw.fields, null, 2)}
            </pre>
          </div>

          {/* LLM Conversation (if available) */}
          {event.llmConversation && (
            <div className="space-y-1.5 pt-1 border-t border-border-subtle">
              <span className="text-[10px] text-text-muted font-mono">LLM Conversation:</span>
              {event.llmConversation.systemPrompt && (
                <div className="rounded bg-slate-500/10 p-2">
                  <span className="text-[9px] text-slate-400 font-mono block mb-1">System:</span>
                  <p className="text-[10px] text-text-secondary font-mono line-clamp-3">
                    {event.llmConversation.systemPrompt}
                  </p>
                </div>
              )}
              {event.llmConversation.userPrompt && (
                <div className="rounded bg-neon-cyan/10 p-2">
                  <span className="text-[9px] text-neon-cyan font-mono block mb-1">User:</span>
                  <p className="text-[10px] text-text-secondary font-mono line-clamp-3">
                    {event.llmConversation.userPrompt}
                  </p>
                </div>
              )}
              {event.llmConversation.assistantResponse && (
                <div className="rounded p-2" style={{ backgroundColor: `${color}10` }}>
                  <span className="text-[9px] font-mono block mb-1" style={{ color }}>Assistant:</span>
                  <p className="text-[10px] text-text-secondary font-mono line-clamp-5">
                    {event.llmConversation.assistantResponse}
                  </p>
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

// ============================================================
//  Conversation Block Component
// ============================================================

interface ConversationBlockProps {
  type: 'system' | 'user' | 'assistant'
  label: string
  content: string
  color?: string
}

const ConversationBlock: React.FC<ConversationBlockProps> = ({ type, label, content, color }) => {
  const config = {
    system: {
      icon: <Bot className="w-3.5 h-3.5" />,
      bg: 'bg-slate-500/10',
      border: 'border-slate-500/30',
      text: 'text-slate-400',
    },
    user: {
      icon: <MessageSquare className="w-3.5 h-3.5" />,
      bg: 'bg-neon-cyan/10',
      border: 'border-neon-cyan/30',
      text: 'text-neon-cyan',
    },
    assistant: {
      icon: <FileText className="w-3.5 h-3.5" />,
      bg: color ? undefined : 'bg-neon-green/10',
      border: color ? undefined : 'border-neon-green/30',
      text: color ? undefined : 'text-neon-green',
    },
  }

  const cfg = config[type]

  return (
    <div
      className={cn(
        "rounded-lg border overflow-hidden",
        cfg.bg,
        cfg.border
      )}
      style={color && type === 'assistant' ? {
        backgroundColor: `${color}10`,
        borderColor: `${color}40`,
      } : undefined}
    >
      <div
        className={cn(
          "flex items-center gap-2 px-3 py-2 text-[10px] font-mono font-semibold border-b",
          cfg.text,
          cfg.border
        )}
        style={color && type === 'assistant' ? {
          color,
          borderColor: `${color}30`,
        } : undefined}
      >
        {cfg.icon}
        {label}
      </div>
      <div className="p-3 max-h-48 overflow-y-auto">
        <pre className="text-xs text-text-secondary whitespace-pre-wrap font-mono leading-relaxed">
          {content}
        </pre>
      </div>
    </div>
  )
}

export default WorkerDetailModal
