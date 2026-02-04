// ============================================================
//  Comment Types - Node Comment Feature
// ============================================================

export interface Comment {
  id: string
  nodeId: string
  sessionId: string
  parentCommentId?: string
  authorId: string
  authorDisplayName: string
  content: string
  createdAt: string
  editedAt?: string
  mentionedUserIds: string[]
  replies: Comment[]
}

export interface CommentPreview {
  id: string
  authorDisplayName: string
  contentPreview: string
  createdAt: string
}

export interface CommentPreviewResult {
  totalCount: number
  comments: CommentPreview[]
}

export interface CreateCommentInput {
  content: string
  parentCommentId?: string
  mentionedUserIds?: string[]
}

export interface UpdateCommentInput {
  content: string
}

export interface CommentPagedResult {
  items: Comment[]
  totalCount: number
}
