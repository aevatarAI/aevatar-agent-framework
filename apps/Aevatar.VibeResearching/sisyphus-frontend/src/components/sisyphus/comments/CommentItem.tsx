// ============================================================
//  CommentItem - Single comment with Cyberpunk styling
// ============================================================

import { memo, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Pencil, Trash2, Reply, Check, X, CornerDownRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { Comment } from '@/types/comments'

const remarkPlugins = [remarkGfm]

interface CommentItemProps {
  comment: Comment
  currentUserId?: string
  isAdmin: boolean
  onReply: (comment: Comment) => void
  onEdit: (comment: Comment) => void
  onDelete: (commentId: string) => void
  isDeleting?: boolean
  depth?: number
}

/** Deterministic color from a string (for avatar backgrounds) */
function nameColor(name: string): { bg: string; border: string; text: string } {
  const colors = [
    { bg: 'rgba(0, 255, 249, 0.12)', border: 'rgba(0, 255, 249, 0.35)', text: '#00fff9' },
    { bg: 'rgba(255, 184, 0, 0.12)', border: 'rgba(255, 184, 0, 0.35)', text: '#ffb800' },
    { bg: 'rgba(168, 85, 247, 0.12)', border: 'rgba(168, 85, 247, 0.35)', text: '#a855f7' },
    { bg: 'rgba(59, 130, 246, 0.12)', border: 'rgba(59, 130, 246, 0.35)', text: '#3b82f6' },
    { bg: 'rgba(34, 197, 94, 0.12)', border: 'rgba(34, 197, 94, 0.35)', text: '#22c55e' },
    { bg: 'rgba(244, 63, 94, 0.12)', border: 'rgba(244, 63, 94, 0.35)', text: '#f43f5e' },
  ]
  let hash = 0
  for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash)
  return colors[Math.abs(hash) % colors.length]
}

/** Format ISO timestamp to relative time */
function formatRelativeTime(isoDate: string): string {
  const diffMs = Date.now() - new Date(isoDate).getTime()
  const diffMin = Math.floor(diffMs / 60_000)
  if (diffMin < 1) return 'just now'
  if (diffMin < 60) return `${diffMin}m ago`
  const diffHr = Math.floor(diffMin / 60)
  if (diffHr < 24) return `${diffHr}h ago`
  const diffDay = Math.floor(diffHr / 24)
  if (diffDay < 30) return `${diffDay}d ago`
  const diffMo = Math.floor(diffDay / 30)
  if (diffMo < 12) return `${diffMo}mo ago`
  return `${Math.floor(diffMo / 12)}y ago`
}

export const CommentItem = memo(function CommentItem({
  comment,
  currentUserId,
  isAdmin,
  onReply,
  onEdit,
  onDelete,
  isDeleting,
  depth = 0,
}: CommentItemProps) {
  const [confirmDelete, setConfirmDelete] = useState(false)
  const isOwner = !!currentUserId && currentUserId === comment.authorId
  const canModify = isOwner || isAdmin
  const isReply = depth > 0
  const firstLetter = (comment.authorDisplayName || '?')[0].toUpperCase()
  const color = nameColor(comment.authorDisplayName || '')

  return (
    <div
      className="group relative transition-all duration-200 mt-1"
      style={isReply ? { marginLeft: `${Math.min(depth, 3) * 24}px` } : undefined}
    >
      {/* Reply thread connector */}
      {isReply && (
        <div className="absolute -left-4 top-4 flex items-center">
          <CornerDownRight className="size-3 text-neon-cyan/25" />
        </div>
      )}

      <div
        className={cn(
          'rounded-lg border transition-all duration-200',
          'bg-[#0c1019]/70 border-[#1a2235]/80',
          'hover:border-[#2a3a55] hover:bg-[#0e1322]/80',
          isReply && 'border-l-2',
        )}
        style={isReply ? { borderLeftColor: `${color.border}` } : undefined}
      >
        <div className="p-3">
          {/* Header row */}
          <div className="flex items-center gap-2.5 mb-2">
            {/* Avatar */}
            <div
              className="flex-shrink-0 size-7 rounded-full flex items-center justify-center text-[10px] font-bold tracking-wide shadow-sm"
              style={{
                background: color.bg,
                border: `1.5px solid ${color.border}`,
                color: color.text,
                boxShadow: `0 0 8px ${color.bg}`,
              }}
            >
              {firstLetter}
            </div>

            {/* Author + badges */}
            <div className="flex items-center gap-1.5 min-w-0 flex-1">
              <span
                className="text-[11px] font-semibold truncate"
                style={{ color: color.text }}
              >
                {comment.authorDisplayName}
              </span>
              {isOwner && (
                <span className="text-[8px] px-1.5 py-0.5 rounded-full bg-neon-cyan/10 text-neon-cyan/70 border border-neon-cyan/15 font-mono uppercase tracking-wider flex-shrink-0">
                  you
                </span>
              )}
            </div>

            {/* Timestamp + edited */}
            <div className="flex items-center gap-1.5 flex-shrink-0">
              {comment.editedAt && (
                <span className="text-[8px] text-text-dimmed/60 italic font-mono">edited</span>
              )}
              <span className="text-[10px] text-text-dimmed/70 font-mono tabular-nums">
                {formatRelativeTime(comment.createdAt)}
              </span>
            </div>
          </div>

          {/* Content */}
          <div
            className={cn(
              'prose prose-sm prose-invert max-w-none text-[11px] leading-relaxed',
              'prose-p:text-text-secondary/90 prose-p:my-0.5',
              'prose-a:text-neon-cyan prose-a:no-underline hover:prose-a:underline',
              'prose-code:text-neon-gold/90 prose-code:text-[10px] prose-code:bg-[#1a1f2e] prose-code:px-1 prose-code:py-0.5 prose-code:rounded',
              'prose-strong:text-text-primary',
              'prose-blockquote:border-l-neon-cyan/30 prose-blockquote:text-text-muted prose-blockquote:not-italic',
              isReply ? 'pl-0' : 'pl-[38px]'
            )}
          >
            <ReactMarkdown remarkPlugins={remarkPlugins}>
              {comment.content}
            </ReactMarkdown>
          </div>

          {/* Actions bar */}
          <div
            className={cn(
              'flex items-center gap-1 mt-2 transition-all duration-200',
              'opacity-0 group-hover:opacity-100',
              isReply ? 'pl-0' : 'pl-[38px]'
            )}
          >
            <button
              onClick={() => onReply(comment)}
              className="flex items-center gap-1 px-2 py-1 text-[10px] text-text-muted/70 hover:text-neon-cyan hover:bg-neon-cyan/5 rounded transition-all font-mono"
            >
              <Reply className="size-3" />
              Reply
            </button>

            {canModify && !confirmDelete && (
              <>
                <button
                  onClick={() => onEdit(comment)}
                  className="flex items-center gap-1 px-2 py-1 text-[10px] text-text-muted/70 hover:text-neon-gold hover:bg-neon-gold/5 rounded transition-all font-mono"
                >
                  <Pencil className="size-3" />
                  Edit
                </button>
                <button
                  onClick={() => setConfirmDelete(true)}
                  className="flex items-center gap-1 px-2 py-1 text-[10px] text-text-muted/70 hover:text-neon-rose hover:bg-neon-rose/5 rounded transition-all font-mono"
                >
                  <Trash2 className="size-3" />
                </button>
              </>
            )}

            {confirmDelete && (
              <div className="flex items-center gap-1 px-2 py-0.5 rounded bg-neon-rose/5 border border-neon-rose/20">
                <span className="text-[10px] text-neon-rose font-mono">Delete?</span>
                <button
                  onClick={() => { onDelete(comment.id); setConfirmDelete(false) }}
                  disabled={isDeleting}
                  className="p-0.5 text-neon-rose hover:bg-neon-rose/15 rounded transition-colors"
                >
                  <Check className="size-3" />
                </button>
                <button
                  onClick={() => setConfirmDelete(false)}
                  className="p-0.5 text-text-muted hover:bg-surface-elevated rounded transition-colors"
                >
                  <X className="size-3" />
                </button>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
})
