// ============================================================
//  CommentList - Paginated comment list with threaded replies
// ============================================================

import { useState, useCallback, useMemo, memo } from 'react'
import { MessageSquare, Loader2, ChevronDown } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useComments, useDeleteComment } from '@/hooks/use-comments'
import { usePermission } from '@/hooks/use-permission'
import { useAuthStore } from '@/store/auth-store'
import type { Comment } from '@/types/comments'
import { CommentItem } from './CommentItem'

const MAX_DEPTH = 3

interface CommentThreadProps {
  comment: Comment
  childrenMap: Map<string, Comment[]>
  depth: number
  currentUserId?: string
  isAdmin: boolean
  onReply: (comment: Comment) => void
  onEdit: (comment: Comment) => void
  onDelete: (commentId: string) => void
  isDeleting?: boolean
}

const CommentThread = memo(function CommentThread({
  comment,
  childrenMap,
  depth,
  currentUserId,
  isAdmin,
  onReply,
  onEdit,
  onDelete,
  isDeleting,
}: CommentThreadProps) {
  const children = childrenMap.get(comment.id)
  const effectiveDepth = Math.min(depth, MAX_DEPTH)

  return (
    <div>
      <CommentItem
        comment={comment}
        currentUserId={currentUserId}
        isAdmin={isAdmin}
        onReply={onReply}
        onEdit={onEdit}
        onDelete={onDelete}
        isDeleting={isDeleting}
        depth={effectiveDepth}
      />
      {children?.map((child) => (
        <CommentThread
          key={child.id}
          comment={child}
          childrenMap={childrenMap}
          depth={depth + 1}
          currentUserId={currentUserId}
          isAdmin={isAdmin}
          onReply={onReply}
          onEdit={onEdit}
          onDelete={onDelete}
          isDeleting={isDeleting}
        />
      ))}
    </div>
  )
})

interface CommentListProps {
  sessionId: string
  nodeId: string
  onReply: (comment: Comment) => void
  onEdit: (comment: Comment) => void
}

export const CommentList = memo(function CommentList({
  sessionId,
  nodeId,
  onReply,
  onEdit,
}: CommentListProps) {
  const [page, setPage] = useState(1)
  const { data, isLoading, isFetching } = useComments(sessionId, nodeId, page)
  const deleteMutation = useDeleteComment(sessionId, nodeId)
  const { isAdmin } = usePermission()
  const currentUserId = useAuthStore((s) => s.user?.id)

  const handleDelete = useCallback(
    (commentId: string) => {
      deleteMutation.mutate(commentId)
    },
    [deleteMutation]
  )

  const comments = data?.items ?? []
  const totalCount = data?.totalCount ?? 0
  const hasMore = comments.length < totalCount && page * 20 < totalCount

  // Build recursive tree: map parentId → children
  const { roots, childrenMap } = useMemo(() => {
    const map = new Map<string, Comment[]>()
    const topLevel: Comment[] = []
    const idSet = new Set(comments.map((c) => c.id))

    for (const c of comments) {
      if (!c.parentCommentId || !idSet.has(c.parentCommentId)) {
        topLevel.push(c)
      } else {
        const arr = map.get(c.parentCommentId) || []
        arr.push(c)
        map.set(c.parentCommentId, arr)
      }
    }

    return { roots: topLevel, childrenMap: map }
  }, [comments])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-10">
        <div className="flex flex-col items-center gap-2">
          <Loader2 className="size-5 text-neon-cyan animate-spin" />
          <span className="text-[10px] text-text-dimmed font-mono">Loading comments...</span>
        </div>
      </div>
    )
  }

  if (comments.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-10 gap-3">
        <div className="relative">
          <div className="size-12 rounded-xl bg-[#0e1322] border border-[#1a2235] flex items-center justify-center">
            <MessageSquare className="size-5 text-text-dimmed/40" />
          </div>
          <div className="absolute -bottom-0.5 -right-0.5 size-4 rounded-full bg-[#0a0e17] border border-[#1a2235] flex items-center justify-center">
            <span className="text-[8px] text-text-dimmed/50 font-mono">0</span>
          </div>
        </div>
        <div className="text-center">
          <p className="text-[11px] text-text-muted font-mono">No comments yet</p>
          <p className="text-[9px] text-text-dimmed/60 mt-0.5">Be the first to share your thoughts on this node</p>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-1.5 max-h-[400px] overflow-y-auto pr-1 scrollbar-thin scrollbar-thumb-[#1a2235] scrollbar-track-transparent">
      {roots.map((comment) => (
        <CommentThread
          key={comment.id}
          comment={comment}
          childrenMap={childrenMap}
          depth={0}
          currentUserId={currentUserId}
          isAdmin={isAdmin}
          onReply={onReply}
          onEdit={onEdit}
          onDelete={handleDelete}
          isDeleting={deleteMutation.isPending}
        />
      ))}

      {/* Load more */}
      {hasMore && (
        <div className="flex justify-center pt-3 pb-1">
          <button
            onClick={() => setPage((p) => p + 1)}
            disabled={isFetching}
            className={cn(
              'flex items-center gap-1.5 px-4 py-1.5 text-[10px] font-mono rounded-lg border transition-all',
              'border-[#1a2235] text-text-muted/70',
              'hover:text-neon-cyan hover:border-neon-cyan/25 hover:bg-neon-cyan/5',
              'disabled:opacity-50'
            )}
          >
            {isFetching ? (
              <Loader2 className="size-3 animate-spin" />
            ) : (
              <ChevronDown className="size-3" />
            )}
            {isFetching ? 'Loading...' : `Load more (${totalCount - comments.length} remaining)`}
          </button>
        </div>
      )}
    </div>
  )
})
