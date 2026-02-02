// ============================================================
//  GitHub OAuth - Redirect Flow
// ============================================================
//
//  GitHub OAuth requires a backend for token exchange (client_secret).
//  This implementation provides:
//  1. Frontend redirect to GitHub authorization
//  2. Callback handling (requires backend proxy)
//
//  For production, you need a backend endpoint like:
//  POST /api/auth/github/callback { code } -> { user, token }
//
// ============================================================

import { oauthConfig, isGitHubConfigured } from './config'
import type { OAuthResult } from './types'
import type { AuthUser } from '@/types/user-management'

// State key for CSRF protection
const STATE_KEY = 'github_oauth_state'

/**
 * Generate random state for CSRF protection
 */
function generateState(): string {
  const array = new Uint8Array(32)
  crypto.getRandomValues(array)
  return Array.from(array, byte => byte.toString(16).padStart(2, '0')).join('')
}

/**
 * Start GitHub OAuth flow (redirect)
 */
export function initiateGitHubLogin(): void {
  if (!isGitHubConfigured()) {
    console.error('[OAuth] GitHub Client ID not configured')
    return
  }

  const state = generateState()
  sessionStorage.setItem(STATE_KEY, state)

  const params = new URLSearchParams({
    client_id: oauthConfig.github.clientId,
    redirect_uri: oauthConfig.github.redirectUri,
    scope: oauthConfig.github.scope,
    state,
  })

  window.location.href = `${oauthConfig.github.authUrl}?${params.toString()}`
}

/**
 * Handle GitHub OAuth callback
 * This should be called when user returns from GitHub authorization
 */
export interface GitHubCallbackResult {
  success: boolean
  user?: AuthUser
  error?: string
}

export async function handleGitHubCallback(
  code: string,
  state: string
): Promise<GitHubCallbackResult> {
  // Verify state for CSRF protection
  const savedState = sessionStorage.getItem(STATE_KEY)
  sessionStorage.removeItem(STATE_KEY)

  if (!savedState || savedState !== state) {
    return {
      success: false,
      error: 'Invalid OAuth state. Please try again.',
    }
  }

  try {
    const response = await fetch('/api/auth/github/callback', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ code, state }),
    })

    if (!response.ok) {
      const error = await response.json().catch(() => ({}))
      return {
        success: false,
        error: error.error || 'GitHub authentication failed',
      }
    }

    const data = await response.json()

    return {
      success: true,
      user: data.user as AuthUser,
    }
  } catch (error) {
    console.error('[OAuth] GitHub callback error:', error)
    return {
      success: false,
      error: 'Failed to complete GitHub authentication',
    }
  }
}

/**
 * Check if current URL is GitHub callback
 */
export function isGitHubCallback(): boolean {
  const url = new URL(window.location.href)
  return (
    url.pathname === '/auth/callback/github' &&
    url.searchParams.has('code') &&
    url.searchParams.has('state')
  )
}

/**
 * Get GitHub callback parameters
 */
export function getGitHubCallbackParams(): { code: string; state: string } | null {
  const url = new URL(window.location.href)
  const code = url.searchParams.get('code')
  const state = url.searchParams.get('state')

  if (!code || !state) return null
  return { code, state }
}

/**
 * Mock GitHub OAuth for development (when backend is not available)
 * This simulates a successful GitHub login
 */
export async function mockGitHubLogin(): Promise<OAuthResult> {
  // Simulate network delay
  await new Promise(resolve => setTimeout(resolve, 1000))

  return {
    success: true,
    user: {
      id: 'github-mock-123',
      email: 'developer@github.local',
      name: 'GitHub Developer',
      picture: 'https://github.com/ghost.png',
      provider: 'github',
    },
  }
}
