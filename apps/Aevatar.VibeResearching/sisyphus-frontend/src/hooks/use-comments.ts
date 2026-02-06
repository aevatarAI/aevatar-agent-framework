// ============================================================
//  useComments - React Query hooks for node comments
// ============================================================

import {
  useQuery,
  useMutation,
  useQueryClient,
  type UseQueryResult,
} from '@tanstack/react-query'
import type {
  CommentPreviewResult,
  CommentPagedResult,
  CreateCommentInput,
  UpdateCommentInput,
  Comment,
} from '@/types/comments'
import {
  getCommentPreview,
  getComments,
  createComment,
  updateComment,
  deleteComment,
} from '@/lib/comments-client'

// === Query Keys ===

const commentKeys = {
  all: ['comments'] as const,
  preview: (sessionId: string, nodeId: string) =>
    [...commentKeys.all, 'preview', sessionId, nodeId] as const,
  list: (sessionId: string, nodeId: string, page: number) =>
    [...commentKeys.all, 'list', sessionId, nodeId, page] as const,
  listsForNode: (sessionId: string, nodeId: string) =>
    [...commentKeys.all, 'list', sessionId, nodeId] as const,
}

// === Queries ===

export function useCommentPreview(
  sessionId: string | undefined,
  nodeId: string | undefined
): UseQueryResult<CommentPreviewResult> {
  return useQuery({
    queryKey: commentKeys.preview(sessionId ?? '', nodeId ?? ''),
    queryFn: () => getCommentPreview(sessionId!, nodeId!),
    enabled: !!sessionId && !!nodeId,
    staleTime: 10_000,
  })
}

export function useComments(
  sessionId: string | undefined,
  nodeId: string | undefined,
  page: number = 1
): UseQueryResult<CommentPagedResult> {
  return useQuery({
    queryKey: commentKeys.list(sessionId ?? '', nodeId ?? '', page),
    queryFn: () => getComments(sessionId!, nodeId!, page),
    enabled: !!sessionId && !!nodeId,
    staleTime: 5_000,
  })
}

// === Mutations ===

export function useCreateComment(sessionId: string, nodeId: string) {
  const queryClient = useQueryClient()

  return useMutation<Comment, Error, CreateCommentInput>({
    mutationFn: (input) => createComment(sessionId, nodeId, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: commentKeys.listsForNode(sessionId, nodeId) })
      queryClient.invalidateQueries({ queryKey: commentKeys.preview(sessionId, nodeId) })
    },
  })
}

export function useUpdateComment(sessionId: string, nodeId: string) {
  const queryClient = useQueryClient()

  return useMutation<Comment, Error, { commentId: string; input: UpdateCommentInput }>({
    mutationFn: ({ commentId, input }) => updateComment(sessionId, nodeId, commentId, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: commentKeys.listsForNode(sessionId, nodeId) })
      queryClient.invalidateQueries({ queryKey: commentKeys.preview(sessionId, nodeId) })
    },
  })
}

export function useDeleteComment(sessionId: string, nodeId: string) {
  const queryClient = useQueryClient()

  return useMutation<void, Error, string>({
    mutationFn: (commentId) => deleteComment(sessionId, nodeId, commentId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: commentKeys.listsForNode(sessionId, nodeId) })
      queryClient.invalidateQueries({ queryKey: commentKeys.preview(sessionId, nodeId) })
    },
  })
}
