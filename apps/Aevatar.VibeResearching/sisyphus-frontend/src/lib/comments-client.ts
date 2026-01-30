// ============================================================
//  Comments Client - API functions for node comments
// ============================================================

import { fetchJson } from './axiom-client'
import type {
  Comment,
  CommentPreviewResult,
  CommentPagedResult,
  CreateCommentInput,
  UpdateCommentInput,
} from '@/types/comments'

function basePath(sessionId: string, nodeId: string): string {
  return `/api/sessions/${encodeURIComponent(sessionId)}/nodes/${encodeURIComponent(nodeId)}/comments`
}

/** Fetch comment preview (count + latest 3) for a node */
export async function getCommentPreview(
  sessionId: string,
  nodeId: string
): Promise<CommentPreviewResult> {
  return fetchJson<CommentPreviewResult>(
    `${basePath(sessionId, nodeId)}/preview`,
    undefined,
    { cache: true, cacheTtl: 10_000 }
  )
}

/** Fetch paginated comments for a node */
export async function getComments(
  sessionId: string,
  nodeId: string,
  page: number = 1,
  pageSize: number = 20
): Promise<CommentPagedResult> {
  const skip = (page - 1) * pageSize
  const take = pageSize
  return fetchJson<CommentPagedResult>(
    `${basePath(sessionId, nodeId)}?skip=${skip}&take=${take}`,
    undefined,
    { cache: false }
  )
}

/** Create a new comment (or reply) */
export async function createComment(
  sessionId: string,
  nodeId: string,
  input: CreateCommentInput
): Promise<Comment> {
  return fetchJson<Comment>(basePath(sessionId, nodeId), {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

/** Update an existing comment */
export async function updateComment(
  sessionId: string,
  nodeId: string,
  commentId: string,
  input: UpdateCommentInput
): Promise<Comment> {
  return fetchJson<Comment>(`${basePath(sessionId, nodeId)}/${encodeURIComponent(commentId)}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

/** Delete a comment */
export async function deleteComment(
  sessionId: string,
  nodeId: string,
  commentId: string
): Promise<void> {
  await fetchJson<void>(`${basePath(sessionId, nodeId)}/${encodeURIComponent(commentId)}`, {
    method: 'DELETE',
  })
}
