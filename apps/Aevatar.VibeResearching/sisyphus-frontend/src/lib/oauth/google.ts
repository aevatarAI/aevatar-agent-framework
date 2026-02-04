// ============================================================
//  Google OAuth - Using Google Identity Services (GIS)
// ============================================================

import { oauthConfig, isGoogleConfigured } from './config'
import type { OAuthResult, GoogleCredentialResponse, GoogleDecodedToken } from './types'

// Google Identity Services types (FedCM compatible)
declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: {
            client_id: string
            callback: (response: GoogleCredentialResponse) => void
            auto_select?: boolean
            cancel_on_tap_outside?: boolean
            use_fedcm_for_prompt?: boolean  // FedCM migration
            itp_support?: boolean            // Intelligent Tracking Prevention
          }) => void
          prompt: (notification?: (notification: {
            isNotDisplayed: () => boolean
            isSkippedMoment: () => boolean
            isDismissedMoment?: () => boolean
            getMomentType?: () => string
            getNotDisplayedReason: () => string
            getSkippedReason: () => string
            getDismissedReason?: () => string
          }) => void) => void
          renderButton: (
            parent: HTMLElement,
            options: {
              type?: 'standard' | 'icon'
              theme?: 'outline' | 'filled_blue' | 'filled_black'
              size?: 'large' | 'medium' | 'small'
              text?: 'signin_with' | 'signup_with' | 'continue_with' | 'signin'
              shape?: 'rectangular' | 'pill' | 'circle' | 'square'
              logo_alignment?: 'left' | 'center'
              width?: number
              locale?: string
            }
          ) => void
          disableAutoSelect: () => void
          cancel: () => void
        }
      }
    }
  }
}

// State
let isInitialized = false
let resolveCallback: ((result: OAuthResult) => void) | null = null

/**
 * Decode JWT token from Google
 */
function decodeJwt(token: string): GoogleDecodedToken | null {
  try {
    const base64Url = token.split('.')[1]
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/')
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    )
    return JSON.parse(jsonPayload)
  } catch {
    return null
  }
}

/**
 * Handle Google credential response
 */
function handleCredentialResponse(response: GoogleCredentialResponse) {
  const decoded = decodeJwt(response.credential)
  
  if (!decoded) {
    resolveCallback?.({
      success: false,
      error: 'Failed to decode Google credential',
    })
    return
  }

  resolveCallback?.({
    success: true,
    user: {
      id: decoded.sub,
      email: decoded.email,
      name: decoded.name,
      picture: decoded.picture,
      provider: 'google',
    },
  })
}

/**
 * Load Google Identity Services SDK
 */
export async function loadGoogleSdk(): Promise<boolean> {
  if (!isGoogleConfigured()) {
    console.warn('[OAuth] Google Client ID not configured')
    return false
  }

  if (window.google?.accounts) {
    return true
  }

  return new Promise((resolve) => {
    const script = document.createElement('script')
    script.src = 'https://accounts.google.com/gsi/client'
    script.async = true
    script.defer = true
    script.onload = () => {
      resolve(true)
    }
    script.onerror = () => {
      console.error('[OAuth] Failed to load Google Identity Services')
      resolve(false)
    }
    document.head.appendChild(script)
  })
}

/**
 * Initialize Google Identity Services
 */
export async function initializeGoogleAuth(): Promise<boolean> {
  if (isInitialized) return true
  
  const loaded = await loadGoogleSdk()
  if (!loaded || !window.google?.accounts) {
    return false
  }

  // FedCM requires Privacy Policy & Terms of Service URLs in Google Console
  // Set use_fedcm_for_prompt: false until Console is properly configured
  const useFedCM = import.meta.env.VITE_GOOGLE_USE_FEDCM === 'true'
  
  window.google.accounts.id.initialize({
    client_id: oauthConfig.google.clientId,
    callback: handleCredentialResponse,
    auto_select: false,
    cancel_on_tap_outside: true,
    use_fedcm_for_prompt: useFedCM,  // Enable FedCM when Console is configured
    itp_support: true,               // Safari ITP support
  })

  isInitialized = true
  return true
}

/**
 * Trigger Google Sign-In popup
 */
export async function signInWithGoogle(): Promise<OAuthResult> {
  if (!isGoogleConfigured()) {
    return {
      success: false,
      error: 'Google OAuth not configured. Please set VITE_GOOGLE_CLIENT_ID.',
    }
  }

  const initialized = await initializeGoogleAuth()
  if (!initialized) {
    return {
      success: false,
      error: 'Failed to initialize Google Sign-In',
    }
  }

  return new Promise((resolve) => {
    resolveCallback = resolve
    
    // Use One Tap prompt with FedCM
    window.google?.accounts.id.prompt((notification) => {
      // FedCM compatible: check moment type if available
      const momentType = notification.getMomentType?.() || 'unknown'
      
      if (notification.isNotDisplayed()) {
        const reason = notification.getNotDisplayedReason()
        console.info('[OAuth] One Tap not displayed:', reason)
        resolve({
          success: false,
          error: `Google Sign-In unavailable: ${reason}. Try using the Google button.`,
        })
      } else if (notification.isSkippedMoment()) {
        const reason = notification.getSkippedReason()
        console.info('[OAuth] One Tap skipped:', reason)
        resolve({
          success: false,
          error: `Google Sign-In skipped: ${reason}. Try using the Google button.`,
        })
      } else if (notification.isDismissedMoment?.()) {
        const reason = notification.getDismissedReason?.() || 'user_cancel'
        console.info('[OAuth] One Tap dismissed:', reason)
        // User dismissed, don't show error - just resolve quietly
        resolve({
          success: false,
          error: reason === 'credential_returned' ? '' : `Sign-in cancelled`,
        })
      }
      // If none of above, credential callback will handle success
      console.debug('[OAuth] One Tap moment:', momentType)
    })

    // Timeout after 60 seconds
    setTimeout(() => {
      if (resolveCallback === resolve) {
        resolveCallback = null
        resolve({
          success: false,
          error: 'Google Sign-In timed out',
        })
      }
    }, 60000)
  })
}

/**
 * Render Google Sign-In button into container
 */
export async function renderGoogleButton(containerId: string): Promise<boolean> {
  const initialized = await initializeGoogleAuth()
  if (!initialized) return false

  const container = document.getElementById(containerId)
  if (!container) {
    console.error(`[OAuth] Container #${containerId} not found`)
    return false
  }

  window.google?.accounts.id.renderButton(container, {
    type: 'standard',
    theme: 'filled_black',
    size: 'large',
    text: 'continue_with',
    shape: 'rectangular',
    width: 300,
  })

  return true
}

/**
 * Sign out from Google
 */
export function signOutGoogle() {
  window.google?.accounts.id.disableAutoSelect()
}
