// ============================================================================
//  ABP API Configuration - Auth Server Connection
// ============================================================================

// === API Base URL ===
// Empty string = use Vite proxy (recommended for development)
// Vite proxy handles: /api/*, /connect/*, /.well-known/*
// Set VITE_API_BASE_URL only for production cross-origin deployment
export const ABP_API_BASE = import.meta.env.VITE_API_BASE_URL || ''

// === Token Storage Keys ===
export const TOKEN_KEY = 'abp_access_token'
export const REFRESH_TOKEN_KEY = 'abp_refresh_token'
export const TOKEN_EXPIRY_KEY = 'abp_token_expiry'

// === Token Management ===

export function getAccessToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function setAccessToken(token: string, expiresIn?: number): void {
  localStorage.setItem(TOKEN_KEY, token)
  if (expiresIn) {
    const expiry = Date.now() + expiresIn * 1000
    localStorage.setItem(TOKEN_EXPIRY_KEY, expiry.toString())
  }
}

export function getRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_TOKEN_KEY)
}

export function setRefreshToken(token: string): void {
  localStorage.setItem(REFRESH_TOKEN_KEY, token)
}

export function clearTokens(): void {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(REFRESH_TOKEN_KEY)
  localStorage.removeItem(TOKEN_EXPIRY_KEY)
}

export function isTokenExpired(): boolean {
  const expiry = localStorage.getItem(TOKEN_EXPIRY_KEY)
  if (!expiry) return true
  return Date.now() > parseInt(expiry, 10)
}

// === ABP Error Response ===

export interface AbpErrorResponse {
  error: {
    code: string
    message: string
    details?: string
    data?: Record<string, unknown>
    validationErrors?: Array<{
      message: string
      members: string[]
    }>
  }
}

// === ABP Paged Result ===

export interface AbpPagedResult<T> {
  items: T[]
  totalCount: number
}

// === ABP Fetch Helper ===

export interface AbpFetchOptions extends RequestInit {
  skipAuth?: boolean
}

export async function abpFetch<T>(
  path: string,
  options: AbpFetchOptions = {}
): Promise<T> {
  const { skipAuth, ...fetchOptions } = options
  
  const headers: HeadersInit = {
    'Content-Type': 'application/json',
    Accept: 'application/json',
    ...fetchOptions.headers,
  }

  // Add authorization header if token exists and not skipped
  if (!skipAuth) {
    const token = getAccessToken()
    if (token) {
      ;(headers as Record<string, string>)['Authorization'] = `Bearer ${token}`
    }
  }

  const response = await fetch(`${ABP_API_BASE}${path}`, {
    ...fetchOptions,
    headers,
  })

  // Handle no content response
  if (response.status === 204) {
    return undefined as T
  }

  // Try to parse JSON
  const text = await response.text()
  let data: T | AbpErrorResponse | undefined

  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      throw new Error(`Invalid JSON response: ${text.substring(0, 100)}`)
    }
  }

  // Handle error responses
  if (!response.ok) {
    const errorData = data as AbpErrorResponse
    if (errorData?.error) {
      const error = new Error(errorData.error.message) as Error & {
        code: string
        details?: string
        validationErrors?: AbpErrorResponse['error']['validationErrors']
      }
      error.code = errorData.error.code
      error.details = errorData.error.details
      error.validationErrors = errorData.error.validationErrors
      throw error
    }
    throw new Error(`API Error ${response.status}: ${text.substring(0, 200)}`)
  }

  return data as T
}

// === Query String Builder ===

export function buildQueryString(params: Record<string, unknown>): string {
  const searchParams = new URLSearchParams()
  
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      searchParams.append(key, String(value))
    }
  }
  
  const query = searchParams.toString()
  return query ? `?${query}` : ''
}
