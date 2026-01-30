// ============================================================
//  CommentEditorPopup - Popup editor for writing/replying/editing comments
// ============================================================

import { useState, useCallback, useEffect, useRef, memo } from 'react'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Send, X, Eye, Pencil } from 'lucide-react'
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

export const CommentEditorPopup = memo(function CommentEditorPopup({
  open,
  mode,
  onClose,
  onSubmit,
  isSubmitting,
}: CommentEditorPopupProps) {
  const [content, setContent] = useState('')
  const [preview, setPreview] = useState(false)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  // Pre-fill for edit mode
  useEffect(() => {
    if (!open) return
    if (mode.type === 'edit') {
      setContent(mode.comment.content)
    } else {
      setContent('')
    }
    setPreview(false)
  }, [open, mode])

  // Auto-focus
  useEffect(() => {
    if (open) {
      setTimeout(() => textareaRef.current?.focus(), 100)
    }
  }, [open])

  const handleSubmit = useCallback(() => {
    const trimmed = content.trim()
    if (!trimmed || isSubmitting) return

    if (mode.type === 'reply') {
      onSubmit(trimmed, mode.replyTo.id)
    } else {
      onSubmit(trimmed)
    }
  }, [content, isSubmitting, mode, onSubmit])

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

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="bg-[#0a0e17]/98 backdrop-blur-xl border border-[#1e2a3e] rounded-xl shadow-2xl text-text-primary overflow-hidden max-w-lg w-[90vw] p-0">
        {/* Header */}
        <DialogHeader className="flex-shrink-0 px-4 pt-4 pb-2 flex flex-row items-center justify-between">
          <DialogTitle className="text-sm font-display text-neon-cyan">
            {title}
          </DialogTitle>
          <button
            onClick={onClose}
            className="p-1.5 rounded-lg text-text-muted hover:text-text-primary hover:bg-surface-elevated transition-colors"
          >
            <X className="size-4" />
          </button>
        </DialogHeader>

        <div className="px-4 pb-4 space-y-3">
          {/* Reply context */}
          {mode.type === 'reply' && (
            <div className="text-[10px] px-2 py-1.5 rounded bg-[#1a1f2e] border border-[#2a3040]">
              <span className="text-text-dimmed font-mono">Replying to:</span>
              <p className="text-text-muted mt-0.5 line-clamp-2">
                {mode.replyTo.content.slice(0, 200)}
              </p>
            </div>
          )}

          {/* Tab bar: Write / Preview */}
          <div className="flex gap-1 border-b border-[#1e2a3e]">
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
            <div
              className="min-h-[150px] rounded-lg p-3 bg-[#0e1220] border border-[#1e2a3e] overflow-y-auto"
            >
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
                {mode.type === 'edit' ? 'Save' : 'Send'}
              </button>
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
})
