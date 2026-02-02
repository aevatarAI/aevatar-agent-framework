// ============================================================
//  CommentEditorPopup - Popup editor for writing/replying/editing comments
//  Uses createPortal to avoid nested Dialog transform issues
// ============================================================

import { useState, useCallback, useEffect, useRef, memo } from 'react'
import { createPortal } from 'react-dom'
import { Send, X, Eye, Pencil, Check } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { cn } from '@/lib/utils'
import type { Comment } from '@/types/comments'

const remarkPlugins = [remarkGfm]
const MAX_LENGTH = 10_000

export type EditorMode =
  | { type: 'create' }
  | { type: 'reply'; replyTo: Comment }
  | { type: 'edit'; comment: Comment }

interface CommentEditorPopupProps {
  open: boolean
  mode: EditorMode
  onClose: () => void
  onSubmit: (content: string, parentId?: string) => void
  isSubmitting?: boolean
}

type SendPhase = 'idle' | 'sending' | 'sent'

export const CommentEditorPopup = memo(function CommentEditorPopup({
  open,
  mode,
  onClose,
  onSubmit,
  isSubmitting,
}: CommentEditorPopupProps) {
  const [content, setContent] = useState('')
  const [preview, setPreview] = useState(false)
  const [sendPhase, setSendPhase] = useState<SendPhase>('idle')
  const [visible, setVisible] = useState(false)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  // Animate in/out
  useEffect(() => {
    if (open) {
      // Mount first, then animate in on next frame
      requestAnimationFrame(() => requestAnimationFrame(() => setVisible(true)))
    } else {
      setVisible(false)
    }
  }, [open])

  // Pre-fill for edit mode
  useEffect(() => {
    if (!open) return
    if (mode.type === 'edit') {
      setContent(mode.comment.content)
    } else {
      setContent('')
    }
    setPreview(false)
    setSendPhase('idle')
  }, [open, mode])

  // Auto-focus + fight Radix focus trap stealing focus back
  useEffect(() => {
    if (!open) return

    const focus = () => textareaRef.current?.focus()
    // Initial focus
    const t1 = setTimeout(focus, 50)
    const t2 = setTimeout(focus, 150)
    const t3 = setTimeout(focus, 300)

    // If Radix steals focus, take it back
    const handleFocusIn = (e: FocusEvent) => {
      const target = e.target as HTMLElement | null
      if (target && !target.closest('[data-comment-editor]')) {
        textareaRef.current?.focus()
      }
    }
    document.addEventListener('focusin', handleFocusIn)

    return () => {
      clearTimeout(t1)
      clearTimeout(t2)
      clearTimeout(t3)
      document.removeEventListener('focusin', handleFocusIn)
    }
  }, [open])

  // Close on Escape
  useEffect(() => {
    if (!open) return
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && sendPhase === 'idle') onClose()
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [open, onClose, sendPhase])

  const handleSubmit = useCallback(() => {
    const trimmed = content.trim()
    if (!trimmed || isSubmitting || sendPhase !== 'idle') return

    setSendPhase('sending')

    setTimeout(() => {
      if (mode.type === 'reply') {
        onSubmit(trimmed, mode.replyTo.id)
      } else {
        onSubmit(trimmed)
      }
    }, 200)

    setTimeout(() => setSendPhase('sent'), 400)

    setTimeout(() => {
      setSendPhase('idle')
      onClose()
    }, 1000)
  }, [content, isSubmitting, mode, onSubmit, onClose, sendPhase])

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        handleSubmit()
      }
    },
    [handleSubmit]
  )

  const title =
    mode.type === 'edit'
      ? 'Edit Comment'
      : mode.type === 'reply'
        ? `Reply to @${mode.replyTo.authorDisplayName}`
        : 'New Comment'

  const charCount = content.length
  const isSending = sendPhase !== 'idle'

  if (!open) return null

  return createPortal(
    <>
      {/* Fullscreen centering wrapper — no translate, pure flexbox */}
      <div
        className="fixed inset-0 z-[200] flex items-center justify-center"
        style={{ pointerEvents: 'auto' }}
        onFocus={(e) => e.stopPropagation()}
        onFocusCapture={(e) => e.stopPropagation()}
        data-comment-editor
      >
        {/* Backdrop */}
        <div
          className={cn(
            'absolute inset-0 bg-black/60 backdrop-blur-sm transition-opacity duration-200',
            visible ? 'opacity-100' : 'opacity-0'
          )}
          onClick={() => { if (!isSending) onClose() }}
        />

        {/* Dialog */}
        <div
          className={cn(
            'relative max-w-lg w-[90vw]',
            'bg-[#0a0e17]/98 backdrop-blur-xl border border-[#1e2a3e] rounded-xl shadow-2xl',
            'text-text-primary overflow-hidden p-0',
            'transition-all duration-200',
            visible
              ? 'opacity-100 scale-100'
              : 'opacity-0 scale-95'
          )}
          role="dialog"
          aria-modal="true"
          aria-label={title}
          onClick={(e) => e.stopPropagation()}
        >
        {/* Send success overlay */}
        <div
          className={cn(
            'absolute inset-0 z-20 flex flex-col items-center justify-center gap-3',
            'bg-[#0a0e17]/95 backdrop-blur-sm rounded-xl transition-all duration-400',
            sendPhase === 'sent'
              ? 'opacity-100 scale-100'
              : 'opacity-0 scale-95 pointer-events-none'
          )}
        >
          <div className="relative flex items-center justify-center">
            <div className={cn(
              'absolute size-16 rounded-full border-2 border-neon-cyan/30 transition-all duration-700',
              sendPhase === 'sent' ? 'scale-100 opacity-0' : 'scale-50 opacity-100'
            )} />
            <div className={cn(
              'size-14 rounded-full bg-neon-cyan/10 border-2 border-neon-cyan/50 flex items-center justify-center transition-all duration-300',
              sendPhase === 'sent'
                ? 'scale-100 shadow-[0_0_24px_rgba(0,255,249,0.25)]'
                : 'scale-75'
            )}>
              <Check className="size-7 text-neon-cyan" strokeWidth={2.5} />
            </div>
          </div>
          <span className="text-sm font-mono text-neon-cyan tracking-wider">
            {mode.type === 'edit' ? 'Saved!' : 'Sent!'}
          </span>
        </div>

        {/* Header */}
        <div className={cn(
          'flex-shrink-0 px-4 pt-4 pb-2 flex flex-row items-center justify-between transition-opacity duration-300',
          isSending && 'opacity-0'
        )}>
          <h2 className="text-sm font-display text-neon-cyan">{title}</h2>
          <button
            onClick={onClose}
            disabled={isSending}
            className="p-1.5 rounded-lg text-text-muted hover:text-text-primary hover:bg-surface-elevated transition-colors disabled:opacity-30"
          >
            <X className="size-4" />
          </button>
        </div>

        <div className="px-4 pb-4 space-y-3">
          {/* Content area — fades out on send */}
          <div
            className={cn(
              'transition-all duration-400 ease-out',
              sendPhase === 'idle'
                ? 'opacity-100'
                : 'opacity-0 pointer-events-none'
            )}
          >
            {/* Reply context */}
            {mode.type === 'reply' && (
              <div className="text-[10px] px-2 py-1.5 rounded bg-[#1a1f2e] border border-[#2a3040] mb-3">
                <span className="text-text-dimmed font-mono">Replying to:</span>
                <p className="text-text-muted mt-0.5 line-clamp-2">
                  {mode.replyTo.content.slice(0, 200)}
                </p>
              </div>
            )}

            {/* Tab bar */}
            <div className="flex gap-1 border-b border-[#1e2a3e] mb-3">
              <button
                onClick={() => setPreview(false)}
                className={cn(
                  'flex items-center gap-1 px-3 py-1.5 text-[10px] font-mono border-b-2 transition-colors -mb-px',
                  !preview
                    ? 'border-neon-cyan text-neon-cyan'
                    : 'border-transparent text-text-muted hover:text-text-secondary'
                )}
              >
                <Pencil className="size-3" />
                Write
              </button>
              <button
                onClick={() => setPreview(true)}
                className={cn(
                  'flex items-center gap-1 px-3 py-1.5 text-[10px] font-mono border-b-2 transition-colors -mb-px',
                  preview
                    ? 'border-neon-cyan text-neon-cyan'
                    : 'border-transparent text-text-muted hover:text-text-secondary'
                )}
              >
                <Eye className="size-3" />
                Preview
              </button>
            </div>

            {/* Editor / Preview */}
            {!preview ? (
              <textarea
                ref={textareaRef}
                value={content}
                onChange={(e) => setContent(e.target.value.slice(0, MAX_LENGTH))}
                onKeyDown={handleKeyDown}
                placeholder="Write your comment... (Markdown supported)"
                rows={6}
                className={cn(
                  'w-full resize-none rounded-lg p-3 text-[11px] leading-relaxed font-mono',
                  'bg-[#0e1220] border border-[#1e2a3e] text-text-primary placeholder:text-text-dimmed',
                  'focus:outline-none focus:border-neon-cyan/50 focus:ring-1 focus:ring-neon-cyan/20',
                  'transition-colors'
                )}
              />
            ) : (
              <div className="min-h-[150px] rounded-lg p-3 bg-[#0e1220] border border-[#1e2a3e] overflow-y-auto">
                {content.trim() ? (
                  <div className="prose prose-sm prose-invert max-w-none text-[11px] leading-relaxed
                    prose-p:text-text-secondary prose-p:my-1
                    prose-a:text-neon-cyan prose-code:text-neon-gold prose-code:text-[10px]
                    prose-strong:text-text-primary"
                  >
                    <ReactMarkdown remarkPlugins={remarkPlugins}>
                      {content}
                    </ReactMarkdown>
                  </div>
                ) : (
                  <p className="text-[11px] text-text-dimmed italic">Nothing to preview</p>
                )}
              </div>
            )}
          </div>

          {/* Footer */}
          <div className="flex items-center justify-between">
            <span
              className={cn(
                'text-[9px] font-mono',
                charCount > MAX_LENGTH * 0.9 ? 'text-neon-rose' : 'text-text-dimmed'
              )}
            >
              {charCount.toLocaleString()}/{MAX_LENGTH.toLocaleString()} · Ctrl+Enter to send
            </span>

            <div className="flex items-center gap-2">
              <button
                onClick={onClose}
                className="px-2.5 py-1 text-[10px] font-mono rounded border border-[#2a3040] text-text-muted hover:text-text-secondary transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSubmit}
                disabled={!content.trim() || isSubmitting || isSending}
                className={cn(
                  'flex items-center gap-1.5 px-3 py-1 text-[10px] font-mono rounded',
                  'border transition-all duration-200',
                  content.trim() && !isSubmitting && !isSending
                    ? 'border-neon-cyan/50 text-neon-cyan bg-neon-cyan/5 hover:bg-neon-cyan/10 hover:shadow-[0_0_12px_rgba(0,255,249,0.2)] active:scale-95'
                    : 'border-[#2a3040] text-text-dimmed cursor-not-allowed'
                )}
              >
                <Send className="size-3" />
                {mode.type === 'edit' ? 'Save' : 'Send'}
              </button>
            </div>
          </div>
        </div>
        </div>
      </div>
    </>,
    document.body
  )
})
