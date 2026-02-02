// ============================================================
//  InlineCommentSection - Comments embedded in node details
// ============================================================

import { useState, useCallback, memo } from 'react'
import { MessageSquare, Plus } from 'lucide-react'
import { useCreateComment, useUpdateComment, useComments } from '@/hooks/use-comments'
import { usePermission } from '@/hooks/use-permission'
import type { Comment } from '@/types/comments'
import { CommentList } from './CommentList'
import { CommentEditorPopup, type EditorMode } from './CommentEditorPopup'

interface InlineCommentSectionProps {
  sessionId: string
  nodeId: string
}

export const InlineCommentSection = memo(function InlineCommentSection({
  sessionId,
  nodeId,
}: InlineCommentSectionProps) {
  const [editorOpen, setEditorOpen] = useState(false)
  const [editorMode, setEditorMode] = useState<EditorMode>({ type: 'create' })
  const { isAuthenticated } = usePermission()
  const createMutation = useCreateComment(sessionId, nodeId)
  const updateMutation = useUpdateComment(sessionId, nodeId)
  const { data } = useComments(sessionId, nodeId, 1)
  const totalCount = data?.totalCount ?? 0

  const handleSubmit = useCallback(
    (content: string, parentId?: string) => {
      if (editorMode.type === 'edit') {
        updateMutation.mutate(
          { commentId: editorMode.comment.id, input: { content } }
        )
      } else {
        createMutation.mutate(
          { content, parentCommentId: parentId }
        )
      }
    },
    [editorMode, createMutation, updateMutation]
  )

  const handleReply = useCallback((comment: Comment) => {
    setEditorMode({ type: 'reply', replyTo: comment })
    setEditorOpen(true)
  }, [])

  const handleEdit = useCallback((comment: Comment) => {
    setEditorMode({ type: 'edit', comment })
    setEditorOpen(true)
  }, [])

  const handleNewComment = useCallback(() => {
    setEditorMode({ type: 'create' })
    setEditorOpen(true)
  }, [])

  return (
    <div className="mt-5 pt-5 border-t border-[#1a2235]">
      {/* Section header */}
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-2.5">
          <div className="relative">
            <MessageSquare className="size-4 text-neon-cyan" />
            {totalCount > 0 && (
              <div className="absolute -top-1.5 -right-2 min-w-[16px] h-4 px-1 rounded-full bg-neon-cyan/15 border border-neon-cyan/30 flex items-center justify-center">
                <span className="text-[8px] font-mono font-bold text-neon-cyan tabular-nums">
                  {totalCount > 99 ? '99+' : totalCount}
                </span>
              </div>
            )}
          </div>
          <span className="text-xs font-mono text-text-primary tracking-wide">
            Discussion
          </span>
          {totalCount > 0 && (
            <div className="h-px flex-1 max-w-[60px] bg-gradient-to-r from-neon-cyan/20 to-transparent" />
          )}
        </div>

        {isAuthenticated ? (
          <button
            onClick={handleNewComment}
            className="flex items-center gap-1.5 px-3 py-1.5 text-[10px] font-mono rounded-lg border border-neon-cyan/25 text-neon-cyan bg-neon-cyan/5 hover:bg-neon-cyan/10 hover:border-neon-cyan/40 hover:shadow-[0_0_12px_rgba(0,255,249,0.1)] transition-all"
          >
            <Plus className="size-3" />
            Add Comment
          </button>
        ) : (
          <a
            href="/login"
            className="flex items-center gap-1 px-3 py-1.5 text-[10px] font-mono rounded-lg border border-[#1a2235] text-text-muted hover:text-neon-cyan hover:border-neon-cyan/20 transition-all"
          >
            Sign in to comment
          </a>
        )}
      </div>

      {/* Comment list */}
      <CommentList
        sessionId={sessionId}
        nodeId={nodeId}
        onReply={handleReply}
        onEdit={handleEdit}
      />

      {/* Editor popup */}
      <CommentEditorPopup
        open={editorOpen}
        mode={editorMode}
        onClose={() => setEditorOpen(false)}
        onSubmit={handleSubmit}
        isSubmitting={createMutation.isPending || updateMutation.isPending}
      />
    </div>
  )
})
