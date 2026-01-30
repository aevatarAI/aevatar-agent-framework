// ============================================================
//  CommentForm - Markdown comment input with Cyberpunk styling
// ============================================================

import { useState, useCallback, useEffect, useRef, memo } from 'react'
import { Send, X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { usePermission } from '@/hooks/use-permission'
import type { Comment } from '@/types/comments'

const MAX_LENGTH = 10_000

export type CommentFormMode =
  | { type: 'create' }
  | { type: 'reply'; replyTo: Comment }
  | { type: 'edit'; comment: Comment }

interface CommentFormProps {
  mode: CommentFormMode
  onSubmit: (content: string, parentId?: string) => void
  onCancel?: () => void
  isSubmitting?: boolean
}

export const CommentForm = memo(function CommentForm({
  mode,
  onSubmit,
  onCancel,
  isSubmitting,
}: CommentFormProps) {
  const { isAuthenticated } = usePermission()
  const [content, setContent] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  // Pre-fill for edit mode
  useEffect(() => {
    if (mode.type === 'edit') {
      setContent(mode.comment.content)
    } else {
      setContent('')
    }
  }, [mode])

  // Auto-focus on mount for reply/edit
  useEffect(() => {
    if (mode.type !== 'create') {
      textareaRef.current?.focus()
    }
  }, [mode.type])

  const handleSubmit = useCallback(() => {
    const trimmed = content.trim()
    if (!trimmed || isSubmitting) return

    if (mode.type === 'reply') {
      onSubmit(trimmed, mode.replyTo.id)
    } else {
      onSubmit(trimmed)
    }
    if (mode.type !== 'edit') setContent('')
  }, [content, isSubmitting, mode, onSubmit])

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        handleSubmit()
      }
      if (e.key === 'Escape' && onCancel) {
        onCancel()
      }
    },
    [handleSubmit, onCancel]
  )

  if (!isAuthenticated) {
    return (
      <div className="p-3 text-center text-[11px] text-text-muted border-t border-[#1e2a3e]">
        <a
          href="/login"
          className="text-neon-cyan hover:underline font-mono"
        >
          Sign in
        </a>{' '}
        to comment
      </div>
    )
  }

  const isEdit = mode.type === 'edit'
  const isReply = mode.type === 'reply'
  const charCount = content.length

  return (
    <div className="border-t border-[#1e2a3e] bg-[#0a0e17]/80 p-3 space-y-2">
      {/* Context banner */}
      {(isReply || isEdit) && (
        <div className="flex items-center justify-between text-[10px] px-2 py-1 rounded bg-[#1a1f2e] border border-[#2a3040]">
          <span className="text-text-muted font-mono">
            {isReply
              ? `Replying to @${mode.replyTo.authorDisplayName}`
              : 'Editing comment'}
          </span>
          {onCancel && (
            <button
              onClick={onCancel}
              className="p-0.5 text-text-muted hover:text-neon-rose transition-colors"
            >
              <X className="size-3" />
            </button>
          )}
        </div>
      )}

      {/* Textarea */}
      <textarea
        ref={textareaRef}
        value={content}
        onChange={(e) => setContent(e.target.value.slice(0, MAX_LENGTH))}
        onKeyDown={handleKeyDown}
        placeholder="Write a comment... (Markdown supported)"
        rows={3}
        className={cn(
          'w-full resize-none rounded-lg p-2.5 text-[11px] leading-relaxed font-mono',
          'bg-[#0e1220] border border-[#1e2a3e] text-text-primary placeholder:text-text-dimmed',
          'focus:outline-none focus:border-neon-cyan/50 focus:ring-1 focus:ring-neon-cyan/20',
          'transition-colors'
        )}
      />

      {/* Footer */}
      <div className="flex items-center justify-between">
        <span
          className={cn(
            'text-[9px] font-mono',
            charCount > MAX_LENGTH * 0.9 ? 'text-neon-rose' : 'text-text-dimmed'
          )}
        >
          {charCount.toLocaleString()}/{MAX_LENGTH.toLocaleString()}
        </span>

        <div className="flex items-center gap-2">
          {onCancel && (
            <button
              onClick={onCancel}
              className="px-2.5 py-1 text-[10px] font-mono rounded border border-[#2a3040] text-text-muted hover:text-text-secondary transition-colors"
            >
              Cancel
            </button>
          )}
          <button
            onClick={handleSubmit}
            disabled={!content.trim() || isSubmitting}
            className={cn(
              'flex items-center gap-1.5 px-3 py-1 text-[10px] font-mono rounded',
              'border transition-all',
              content.trim() && !isSubmitting
                ? 'border-neon-cyan/50 text-neon-cyan bg-neon-cyan/5 hover:bg-neon-cyan/10 hover:shadow-[0_0_12px_rgba(0,255,249,0.2)]'
                : 'border-[#2a3040] text-text-dimmed cursor-not-allowed'
            )}
          >
            <Send className="size-3" />
            {isEdit ? 'Save' : 'Send'}
          </button>
        </div>
      </div>
    </div>
  )
})
