// ============================================================
//  Event Card - Individual Event Display
//  Supports multiple event types with different styles
// ============================================================

import React, { useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import {
  Check, Vote, Zap, Flag, Loader2, X, Terminal,
  Search, Globe, FileText, ChevronDown, Copy, MessageSquare
} from 'lucide-react'
import { cn } from '@/lib/utils'
import type { ClassifiedEvent, EventCategory } from '@/types'

interface EventCardProps {
  event: ClassifiedEvent
}

// Category styles configuration
const CATEGORY_STYLES: Record<EventCategory, {
  bg: string
  border: string
  icon: React.ReactNode
  label: string
  labelColor: string
}> = {
  consensus: {
    bg: 'bg-gradient-to-r from-neon-green/10 to-neon-green/5',
    border: 'border-neon-green/40',
    icon: <Check className="w-4 h-4 text-neon-green" />,
    label: 'CONSENSUS',
    labelColor: 'text-neon-green bg-neon-green/20',
  },
  proposal: {
    bg: 'bg-gradient-to-r from-neon-cyan/10 to-neon-cyan/5',
    border: 'border-neon-cyan/40',
    icon: <Zap className="w-4 h-4 text-neon-cyan" />,
    label: 'PROPOSAL',
    labelColor: 'text-neon-cyan bg-neon-cyan/20',
  },
  vote: {
    bg: 'bg-gradient-to-r from-neon-gold/10 to-neon-gold/5',
    border: 'border-neon-gold/40',
    icon: <Vote className="w-4 h-4 text-neon-gold" />,
    label: 'VOTE',
    labelColor: 'text-neon-gold bg-neon-gold/20',
  },
  tool_call: {
    bg: 'bg-gradient-to-r from-neon-rose/10 to-neon-rose/5',
    border: 'border-neon-rose/40',
    icon: <Terminal className="w-4 h-4 text-neon-rose" />,
    label: 'TOOL',
    labelColor: 'text-neon-rose bg-neon-rose/20',
  },
  red_flag: {
    bg: 'bg-gradient-to-r from-red-500/10 to-red-500/5',
    border: 'border-red-500/40',
    icon: <Flag className="w-4 h-4 text-red-500" />,
    label: 'RED FLAG',
    labelColor: 'text-red-500 bg-red-500/20',
  },
  parallel: {
    bg: 'bg-gradient-to-r from-neon-purple/10 to-neon-purple/5',
    border: 'border-neon-purple/40',
    icon: <Zap className="w-4 h-4 text-neon-purple" />,
    label: 'PARALLEL',
    labelColor: 'text-neon-purple bg-neon-purple/20',
  },
  llm: {
    bg: 'bg-gradient-to-r from-blue-400/10 to-blue-400/5',
    border: 'border-blue-400/40',
    icon: <MessageSquare className="w-4 h-4 text-blue-400" />,
    label: 'LLM',
    labelColor: 'text-blue-400 bg-blue-400/20',
  },
}

// Status icon mapping
const STATUS_ICONS: Record<string, React.ReactNode> = {
  pending: <Loader2 className="w-3.5 h-3.5 text-neon-gold animate-spin" />,
  running: <Loader2 className="w-3.5 h-3.5 text-neon-cyan animate-spin" />,
  completed: <Check className="w-3.5 h-3.5 text-neon-green" />,
  failed: <X className="w-3.5 h-3.5 text-red-500" />,
}

// Tool icon mapping
const TOOL_ICONS: Record<string, React.ReactNode> = {
  web_search: <Search className="w-4 h-4" />,
  code_execute: <Terminal className="w-4 h-4" />,
  api_call: <Globe className="w-4 h-4" />,
  file_read: <FileText className="w-4 h-4" />,
  default: <Terminal className="w-4 h-4" />,
}

const EventCard: React.FC<EventCardProps> = ({ event }) => {
  const [expanded, setExpanded] = useState(false)
  const style = CATEGORY_STYLES[event.category]
  const statusIcon = STATUS_ICONS[event.status] || STATUS_ICONS.pending

  const formatTime = (timestamp: number) => {
    const date = new Date(timestamp)
    return date.toLocaleTimeString('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    })
  }

  const hasExpandableContent = 
    event.llmConversation?.systemPrompt ||
    event.llmConversation?.userPrompt ||
    event.llmConversation?.assistantResponse

  return (
    <motion.div
      layout
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      className={cn(
        "rounded-lg border p-3 transition-all",
        style.bg,
        style.border,
        hasExpandableContent && "cursor-pointer hover:shadow-md"
      )}
      onClick={() => hasExpandableContent && setExpanded(!expanded)}
    >
      {/* Header */}
      <div className="flex items-start gap-3">
        {/* Icon */}
        <div className="flex-shrink-0 w-8 h-8 rounded-lg bg-bg-base/50 flex items-center justify-center">
          {event.category === 'tool_call' && event.toolInfo
            ? (TOOL_ICONS[event.toolInfo.toolName] || TOOL_ICONS.default)
            : style.icon}
        </div>

        {/* Content */}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            {/* Label */}
            <span className={cn("px-1.5 py-0.5 text-[9px] font-mono font-semibold rounded", style.labelColor)}>
              {style.label}
            </span>
            {/* Status */}
            <span className="flex items-center gap-1">
              {statusIcon}
            </span>
            {/* Time */}
            <span className="text-[10px] text-text-muted font-mono ml-auto">
              {formatTime(event.timestamp)}
            </span>
          </div>

          {/* Title */}
          <h4 className="text-sm font-medium text-text-primary truncate">
            {event.title}
          </h4>

          {/* Message */}
          <p className="text-xs text-text-muted mt-0.5 line-clamp-2">
            {event.message}
          </p>

          {/* Vote Info */}
          {event.voteInfo && (
            <div className="flex items-center gap-3 mt-2 text-[10px] text-text-muted font-mono">
              <span>Round: {event.voteInfo.round}/{event.voteInfo.maxRounds}</span>
              <span>K: {event.voteInfo.k}</span>
              <span>Votes: {event.voteInfo.currentVotes}</span>
              {event.voteInfo.mode && (
                <span className={cn(
                  "px-1 py-0.5 rounded",
                  event.voteInfo.mode === 'semantic' ? "bg-neon-cyan/20 text-neon-cyan" : "bg-text-muted/20"
                )}>
                  {event.voteInfo.mode}
                </span>
              )}
            </div>
          )}

          {/* Tool Info */}
          {event.toolInfo && (
            <div className="flex items-center gap-3 mt-2 text-[10px] text-text-muted font-mono">
              <span>{event.toolInfo.toolName}</span>
              {event.toolInfo.durationMs && (
                <span>{event.toolInfo.durationMs}ms</span>
              )}
              {event.toolInfo.error && (
                <span className="text-red-500">{event.toolInfo.error}</span>
              )}
            </div>
          )}

          {/* Red Flag Info */}
          {event.redFlagInfo && (
            <div className="mt-2 p-2 bg-red-500/10 border border-red-500/30 rounded text-xs text-red-400">
              {event.redFlagInfo.reason}
            </div>
          )}
        </div>

        {/* Expand indicator */}
        {hasExpandableContent && (
          <ChevronDown className={cn(
            "w-4 h-4 text-text-muted transition-transform",
            expanded && "rotate-180"
          )} />
        )}
      </div>

      {/* Expanded LLM Conversation */}
      <AnimatePresence>
        {expanded && event.llmConversation && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="overflow-hidden"
          >
            <div className="mt-3 pt-3 border-t border-border-subtle space-y-2">
              {event.llmConversation.systemPrompt && (
                <ConversationBlock
                  type="system"
                  content={event.llmConversation.systemPrompt}
                />
              )}
              {event.llmConversation.userPrompt && (
                <ConversationBlock
                  type="user"
                  content={event.llmConversation.userPrompt}
                />
              )}
              {event.llmConversation.assistantResponse && (
                <ConversationBlock
                  type="assistant"
                  content={event.llmConversation.assistantResponse}
                />
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </motion.div>
  )
}

// LLM Conversation Block
interface ConversationBlockProps {
  type: 'system' | 'user' | 'assistant'
  content: string
}

const ConversationBlock: React.FC<ConversationBlockProps> = ({ type, content }) => {
  const [copied, setCopied] = useState(false)

  const styles = {
    system: 'bg-bg-surface border-border-subtle',
    user: 'bg-neon-cyan/5 border-neon-cyan/30',
    assistant: 'bg-neon-green/5 border-neon-green/30',
  }

  const labels = {
    system: 'System',
    user: 'User',
    assistant: 'Assistant',
  }

  const handleCopy = (e: React.MouseEvent) => {
    e.stopPropagation()
    navigator.clipboard.writeText(content)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  return (
    <div className={cn("rounded-lg border p-2", styles[type])}>
      <div className="flex items-center justify-between mb-1">
        <span className="text-[10px] font-mono text-text-muted uppercase">
          {labels[type]}
        </span>
        <button
          onClick={handleCopy}
          className="p-1 text-text-muted hover:text-text-primary transition-colors"
        >
          {copied ? <Check className="w-3 h-3 text-neon-green" /> : <Copy className="w-3 h-3" />}
        </button>
      </div>
      <pre className="text-xs text-text-secondary whitespace-pre-wrap font-mono max-h-32 overflow-y-auto">
        {content.length > 500 ? content.slice(0, 500) + '...' : content}
      </pre>
    </div>
  )
}

export default EventCard
