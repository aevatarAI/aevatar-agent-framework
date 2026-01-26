// ============================================================================
//  Axiom Client - Fetch Helper & Request Caching
// ============================================================================

import { apiLogger } from '../logger'

// === API Base URL ===
export const API_BASE = import.meta.env.VITE_AXIOM_API_BASE || ''

// === Cache Types ===

interface CacheEntry<T> {
  data: T
  timestamp: number
}

interface PendingRequest<T> {
  promise: Promise<T>
  abortController: AbortController
}

// === Cache State ===

const requestCache = new Map<string, CacheEntry<unknown>>()
const CACHE_TTL_MS = 5000
const pendingRequests = new Map<string, PendingRequest<unknown>>()

let currentSessionAbortController: AbortController | null = null

// === Abort Controller Management ===

export function getSessionAbortController(): AbortController {
  if (!currentSessionAbortController) {
    currentSessionAbortController = new AbortController()
  }
  return currentSessionAbortController
}

export function abortCurrentSessionRequests(): void {
  if (currentSessionAbortController) {
    currentSessionAbortController.abort()
  }
  currentSessionAbortController = new AbortController()
  pendingRequests.clear()
}

export function clearRequestCache(): void {
  requestCache.clear()
}

// === Fetch Helper ===

export interface FetchOptions {
  cache?: boolean
  cacheTtl?: number
}

export async function fetchJson<T>(
  path: string,
  init?: RequestInit,
  options?: FetchOptions
): Promise<T> {
  const cacheKey = `${init?.method || 'GET'}:${path}`
  const useCaching = options?.cache !== false && (!init?.method || init.method === 'GET')
  const ttl = options?.cacheTtl ?? CACHE_TTL_MS

  // Check cache first
  if (useCaching) {
    const cached = requestCache.get(cacheKey) as CacheEntry<T> | undefined
    if (cached && Date.now() - cached.timestamp < ttl) {
      return cached.data
    }
  }

  // Check for pending identical request (deduplication)
  if (useCaching && pendingRequests.has(cacheKey)) {
    return pendingRequests.get(cacheKey)!.promise as Promise<T>
  }

  // Create abort controller linked to session controller
  const abortController = new AbortController()
  const sessionController = getSessionAbortController()

  const abortHandler = () => abortController.abort()
  sessionController.signal.addEventListener('abort', abortHandler)

  const fetchPromise = (async () => {
    try {
      const res = await fetch(`${API_BASE}${path}`, {
        ...init,
        signal: abortController.signal,
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
          ...init?.headers,
        },
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(`API Error ${res.status}: ${text}`)
      }
      const data = (await res.json()) as T

      if (useCaching) {
        requestCache.set(cacheKey, { data, timestamp: Date.now() })
      }

      return data
    } finally {
      sessionController.signal.removeEventListener('abort', abortHandler)
      pendingRequests.delete(cacheKey)
    }
  })()

  if (useCaching) {
    pendingRequests.set(cacheKey, { promise: fetchPromise, abortController })
  }

  return fetchPromise
}

// === Session ID Validator ===

export function validateSessionId(
  sessionId: string | null | undefined,
  methodName: string
): sessionId is string {
  if (!sessionId) {
    apiLogger.warn(`${methodName} called with invalid sessionId:`, sessionId)
    return false
  }
  return true
}
