// ============================================================================
//  Axiom Client - Fetch Helper & Request Caching
//  Supports 401 auto-refresh with retry
// ============================================================================

import { apiLogger } from '../logger'
import { getAccessToken, clearTokens } from '../abp/config'
import { abpRefreshToken } from '../abp/auth'

// === API Base URL ===
export const API_BASE = import.meta.env.VITE_API_BASE_URL || ''

// === Token Refresh State ===
let isRefreshing = false
let refreshPromise: Promise<boolean> | null = null

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
  skipAuthRefresh?: boolean // Skip 401 auto-refresh (for auth endpoints)
}

// === Core Fetch with 401 Auto-Refresh ===

async function doFetch(
  path: string,
  init: RequestInit | undefined,
  abortController: AbortController
): Promise<Response> {
  const token = getAccessToken()
  const authHeaders: Record<string, string> = token
    ? { Authorization: `Bearer ${token}` }
    : {}

  return fetch(`${API_BASE}${path}`, {
    ...init,
    signal: abortController.signal,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
      ...authHeaders,
      ...init?.headers,
    },
  })
}

async function handleTokenRefresh(): Promise<boolean> {
  // If already refreshing, wait for that to complete
  if (isRefreshing && refreshPromise) {
    return refreshPromise
  }

  isRefreshing = true
  refreshPromise = abpRefreshToken()
    .then((success) => {
      if (!success) {
        clearTokens()
        // Dispatch event to notify app of auth failure
        window.dispatchEvent(new CustomEvent('auth:session-expired'))
      }
      return success
    })
    .finally(() => {
      isRefreshing = false
      refreshPromise = null
    })

  return refreshPromise
}

export async function fetchJson<T>(
  path: string,
  init?: RequestInit,
  options?: FetchOptions
): Promise<T> {
  const cacheKey = `${init?.method || 'GET'}:${path}`
  const useCaching = options?.cache !== false && (!init?.method || init.method === 'GET')
  const ttl = options?.cacheTtl ?? CACHE_TTL_MS
  const skipAuthRefresh = options?.skipAuthRefresh ?? false

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
      let res = await doFetch(path, init, abortController)

      // Handle 401: attempt token refresh and retry once
      if (res.status === 401 && !skipAuthRefresh) {
        apiLogger.info(`[fetchJson] 401 on ${path}, attempting token refresh...`)
        
        const refreshed = await handleTokenRefresh()
        if (refreshed) {
          apiLogger.info(`[fetchJson] Token refreshed, retrying ${path}...`)
          // Retry with new token
          res = await doFetch(path, init, abortController)
        } else {
          apiLogger.warn(`[fetchJson] Token refresh failed for ${path}`)
          const text = await res.text()
          throw new Error(`API Error 401: ${text || 'Unauthorized - Session expired'}`)
        }
      }

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
