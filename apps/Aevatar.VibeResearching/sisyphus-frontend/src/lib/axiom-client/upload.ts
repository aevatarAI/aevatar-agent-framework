// ============================================================================
//  Axiom Client - File Upload APIs
// ============================================================================

import { apiLogger } from '../logger'
import { getSessionAbortController, API_BASE } from './fetch'
import type { UploadExtractionResponse } from './types'

// === Upload with Knowledge Extraction ===

export async function uploadWithExtraction(
  sessionId: string | null | undefined,
  file: File,
  options?: {
    providerName?: string
    maxKnowledgePoints?: number
  }
): Promise<UploadExtractionResponse> {
  if (!sessionId) {
    apiLogger.warn('uploadWithExtraction called with invalid sessionId:', sessionId)
    return { ok: false, error: 'Invalid sessionId' }
  }

  if (!file) {
    apiLogger.warn('uploadWithExtraction called with no file')
    return { ok: false, error: 'No file provided' }
  }

  const formData = new FormData()
  formData.append('file', file)

  const params = new URLSearchParams()
  if (options?.providerName) {
    params.set('providerName', options.providerName)
  }
  if (options?.maxKnowledgePoints) {
    params.set('maxKnowledgePoints', String(options.maxKnowledgePoints))
  }

  const queryString = params.toString()
  const url = `${API_BASE}/api/sessions/${encodeURIComponent(sessionId)}/uploads/extract${queryString ? `?${queryString}` : ''}`

  try {
    const sessionController = getSessionAbortController()
    const res = await fetch(url, {
      method: 'POST',
      body: formData,
      signal: sessionController.signal,
    })

    if (!res.ok) {
      const text = await res.text()
      return { ok: false, error: `Upload failed: ${res.status} ${text}` }
    }

    const result = (await res.json()) as UploadExtractionResponse
    return result
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unknown error'
    apiLogger.error('uploadWithExtraction error:', message)
    return { ok: false, error: message }
  }
}
