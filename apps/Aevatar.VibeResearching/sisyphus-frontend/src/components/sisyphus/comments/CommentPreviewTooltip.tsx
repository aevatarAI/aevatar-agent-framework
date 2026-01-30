// ============================================================
//  CommentPreviewTooltip - Hover preview for node comments
// ============================================================

import { memo } from 'react'
import { MessageSquare, ArrowRight, Loader2 } from 'lucide-react'
import { useCommentPreview } from '@/hooks/use-comments'
import { usePermission } from '@/hooks/use-permission'

/** Format ISO date to short relative string */
function shortRelative(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const min = Math.floor(diff / 60_000)
  if (min < 1) return 'now'
  if (min < 60) return `${min}m`
  const hr = Math.floor(min / 60)
  if (hr < 24) return `${hr}h`
  return `${Math.floor(hr / 24)}d`
}

interface CommentPreviewTooltipProps {
  sessionId: string
  nodeId: string
  onViewAll: () => void
}

export const CommentPreviewTooltip = memo(function CommentPreviewTooltip({
  sessionId,
  nodeId,
  onViewAll,
}: CommentPreviewTooltipProps) {
  const { data, isLoading } = useCommentPreview(sessionId, nodeId)
  const { isAuthenticated } = usePermission()

  if (isLoading) {
    return (
      <div className="p-3 flex items-center justify-center">
        <Loader2 className="size-4 text-neon-cyan animate-spin" />
      </div>
    )
  }

  const totalCount = data?.totalCount ?? 0
  const latestComments = data?.comments ?? []

  return (
    <div
      className="w-[260px] rounded-lg overflow-hidden"
      style={{
        background: 'rgba(10, 14, 23, 0.96)',
        border: '1px solid rgba(0, 255, 249, 0.15)',
        boxShadow: '0 0 20px rgba(0, 255, 249, 0.08), 0 4px 16px rgba(0, 0, 0, 0.4)',
      }}
    >
      {/* Header */}
      <div className="px-3 py-2 border-b border-[#1e2a3e] flex items-center gap-2">
        <MessageSquare className="size-3.5 text-neon-cyan" />
        <span className="text-[11px] font-mono text-text-primary">
          {totalCount} comment{totalCount !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Latest comments */}
      {latestComments.length > 0 ? (
        <div className="divide-y divide-[#1a1f2e]">
          {latestComments.map((c, i) => (
            <div key={i} className="px-3 py-2">
              <div className="flex items-center justify-between mb-0.5">
                <span className="text-[10px] font-medium text-text-secondary truncate max-w-[160px]">
                  {c.authorDisplayName}
                </span>
                <span className="text-[9px] text-text-dimmed font-mono">
                  {shortRelative(c.createdAt)}
                </span>
              </div>
              <p className="text-[10px] text-text-muted line-clamp-2 leading-relaxed">
                {c.contentPreview}
              </p>
            </div>
          ))}
        </div>
      ) : (
        <div className="px-3 py-4 text-center">
          <p className="text-[10px] text-text-muted">No comments yet</p>
          {isAuthenticated && (
            <p className="text-[9px] text-text-dimmed mt-1">Be the first to comment</p>
          )}
        </div>
      )}

      {/* Footer */}
      <div className="px-3 py-2 border-t border-[#1e2a3e]">
        <button
          onClick={onViewAll}
          className="w-full flex items-center justify-center gap-1.5 text-[10px] font-mono text-neon-cyan hover:text-neon-cyan/80 transition-colors"
        >
          View all comments
          <ArrowRight className="size-3" />
        </button>
      </div>
    </div>
  )
})
