// ============================================================
//  CommentDialog - Full comment view with node content
// ============================================================

import { useState, useCallback, memo } from 'react'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog'
import { MarkdownPreview } from '@/components/ui/markdown-preview'
import { X } from 'lucide-react'
import { useCreateComment, useUpdateComment } from '@/hooks/use-comments'
import type { Comment } from '@/types/comments'
import { CommentList } from './CommentList'
import { CommentForm, type CommentFormMode } from './CommentForm'

interface CommentDialogProps {
  open: boolean
  onClose: () => void
  sessionId: string
  nodeId: string
  nodeTitle: string
  nodeContent?: string
}

export const CommentDialog = memo(function CommentDialog({
  open,
  onClose,
  sessionId,
  nodeId,
  nodeTitle,
  nodeContent,
}: CommentDialogProps) {
  const [formMode, setFormMode] = useState<CommentFormMode>({ type: 'create' })
  const createMutation = useCreateComment(sessionId, nodeId)
  const updateMutation = useUpdateComment(sessionId, nodeId)

  const handleSubmit = useCallback(
    (content: string, parentId?: string) => {
      if (formMode.type === 'edit') {
        updateMutation.mutate(
          { commentId: formMode.comment.id, input: { content } },
          { onSuccess: () => setFormMode({ type: 'create' }) }
        )
      } else {
        createMutation.mutate(
          { content, parentCommentId: parentId },
          { onSuccess: () => setFormMode({ type: 'create' }) }
        )
      }
    },
    [formMode, createMutation, updateMutation]
  )

  const handleReply = useCallback((comment: Comment) => {
    setFormMode({ type: 'reply', replyTo: comment })
  }, [])

  const handleEdit = useCallback((comment: Comment) => {
    setFormMode({ type: 'edit', comment })
  }, [])

  const handleCancel = useCallback(() => {
    setFormMode({ type: 'create' })
  }, [])

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose() }}>
      <DialogContent className="bg-[#0a0e17]/98 backdrop-blur-xl border border-[#1e2a3e] rounded-xl shadow-2xl text-text-primary overflow-hidden max-w-3xl w-[90vw] max-h-[85vh] flex flex-col p-0">
        {/* Header */}
        <DialogHeader className="flex-shrink-0 p-4 border-b border-[#1e2a3e] flex flex-row items-start justify-between">
          <div className="min-w-0 flex-1">
            <DialogTitle className="text-sm font-display text-neon-cyan truncate">
              {nodeTitle}
            </DialogTitle>
            <DialogDescription className="text-[10px] font-mono text-text-dimmed mt-0.5 truncate">
              {nodeId}
            </DialogDescription>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 rounded-lg text-text-muted hover:text-text-primary hover:bg-surface-elevated transition-colors flex-shrink-0 ml-2"
          >
            <X className="size-4" />
          </button>
        </DialogHeader>

        {/* Node content preview */}
        {nodeContent && (
          <div className="flex-shrink-0 max-h-[30vh] overflow-y-auto border-b border-[#1e2a3e]">
            <MarkdownPreview content={nodeContent} title={nodeTitle} maxHeight="max-h-[28vh]" />
          </div>
        )}

        {/* Comments list */}
        <div className="flex-1 min-h-0 overflow-y-auto">
          <CommentList
            sessionId={sessionId}
            nodeId={nodeId}
            onReply={handleReply}
            onEdit={handleEdit}
          />
        </div>

        {/* Comment form (fixed at bottom) */}
        <div className="flex-shrink-0">
          <CommentForm
            mode={formMode}
            onSubmit={handleSubmit}
            onCancel={formMode.type !== 'create' ? handleCancel : undefined}
            isSubmitting={createMutation.isPending || updateMutation.isPending}
          />
        </div>
      </DialogContent>
    </Dialog>
  )
})
