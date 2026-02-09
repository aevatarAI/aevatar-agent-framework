// ============================================================
//  Google OAuth - Redirect Flow (Backend Token Exchange)
// ============================================================
//
//  This implementation uses the standard OAuth 2.0 authorization
//  code flow with backend token exchange, similar to GitHub OAuth.
//
//  Flow:
//  1. Frontend redirects to Google authorization
//  2. Google redirects back with authorization code
//  3. Frontend sends code to backend
//  4. Backend exchanges code for tokens and creates user
//
// ============================================================

import { oauthConfig, isGoogleConfigured } from './config'
import type { OAuthResult } from './types'
import type { AuthUser } from '@/types/user-management'

// State key for CSRF protection
const STATE_KEY = 'google_oauth_state'

/**
 * Generate random state for CSRF protection
 */
function generateState(): string {
  const array = new Uint8Array(32)
  crypto.getRandomValues(array)
  return Array.from(array, byte => byte.toString(16).padStart(2, '0')).join('')
}

/**
 * Start Google OAuth flow (redirect)
 */
export function initiateGoogleLogin(): void {
  if (!isGoogleConfigured()) {
    console.error('[OAuth] Google Client ID not configured')
    return
  }

  const state = generateState()
  sessionStorage.setItem(STATE_KEY, state)

  const params = new URLSearchParams({
    client_id: oauthConfig.google.clientId,
    redirect_uri: oauthConfig.google.redirectUri,
    response_type: 'code',
    scope: oauthConfig.google.scope,
    state,
    access_type: 'offline',      // Get refresh token
    prompt: 'select_account',    // Always show account picker
  })

  window.location.href = `https://accounts.google.com/o/oauth2/v2/auth?${params.toString()}`
}

/**
 * Handle Google OAuth callback
 * This should be called when user returns from Google authorization
 */
export interface GoogleCallbackResult {
  success: boolean
  user?: AuthUser
  error?: string
}

export async function handleGoogleCallback(
  code: string,
  state: string
): Promise<GoogleCallbackResult> {
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
    const response = await fetch('/api/auth/google/callback', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ code, state }),
    })

    if (!response.ok) {
      const error = await response.json().catch(() => ({}))
      return {
        success: false,
        error: error.error || 'Google authentication failed',
      }
    }

    const data = await response.json()

    return {
      success: true,
      user: data.user as AuthUser,
    }
  } catch (error) {
    console.error('[OAuth] Google callback error:', error)
    return {
      success: false,
      error: 'Failed to complete Google authentication',
    }
  }
}

/**
 * Check if current URL is Google callback
 */
export function isGoogleCallback(): boolean {
  const url = new URL(window.location.href)
  return (
    url.pathname === '/auth/callback/google' &&
    url.searchParams.has('code') &&
    url.searchParams.has('state')
  )
}

/**
 * Get Google callback parameters
 */
export function getGoogleCallbackParams(): { code: string; state: string } | null {
  const url = new URL(window.location.href)
  const code = url.searchParams.get('code')
  const state = url.searchParams.get('state')

  if (!code || !state) return null
  return { code, state }
}

/**
 * Mock Google OAuth for development (when backend is not available)
 * This simulates a successful Google login
 */
export async function mockGoogleLogin(): Promise<OAuthResult> {
  // Simulate network delay
  await new Promise(resolve => setTimeout(resolve, 1000))

  return {
    success: true,
    user: {
      id: 'google-mock-123',
      email: 'developer@google.local',
      name: 'Google Developer',
      picture: 'https://lh3.googleusercontent.com/a/default-user',
      provider: 'google',
    },
  }
}

// ============================================================
//  Legacy exports for backward compatibility (deprecated)
// ============================================================

/** @deprecated Use initiateGoogleLogin instead */
export async function signInWithGoogle(): Promise<OAuthResult> {
  // Redirect flow - this function now initiates redirect
  initiateGoogleLogin()
  // Return pending state since we're redirecting
  return {
    success: false,
    error: 'Redirecting to Google...',
  }
}

/** @deprecated No longer needed with redirect flow */
export async function loadGoogleSdk(): Promise<boolean> {
  return true // No SDK needed for redirect flow
}

/** @deprecated No longer needed with redirect flow */
export async function initializeGoogleAuth(): Promise<boolean> {
  return true // No initialization needed for redirect flow
}

/** @deprecated No longer needed with redirect flow */
export async function renderGoogleButton(_containerId: string): Promise<boolean> {
  console.warn('[OAuth] renderGoogleButton is deprecated. Use initiateGoogleLogin instead.')
  return false
}

/** @deprecated No longer needed with redirect flow */
export function signOutGoogle() {
  // No-op for redirect flow
}
