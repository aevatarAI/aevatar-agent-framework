// ============================================================================
//  ABP Auth API - Authentication (Cookie + Token Hybrid)
//  Supports both Cookie-based login and OpenIddict Token flow
// ============================================================================

import type { AuthUser, LoginInput, RegisterInput } from '@/types/user-management'
import {
  ABP_API_BASE,
  abpFetch,
  setAccessToken,
  setRefreshToken,
  clearTokens,
  getAccessToken,
  getRefreshToken,
} from './config'
import { usePermissionStore } from '@/store/permission-store'

// ============================================================================
//  Types
// ============================================================================

interface TokenResponse {
  access_token: string
  token_type: string
  expires_in: number
  refresh_token?: string
  scope?: string
}

interface VibeProfileResponse {
  id: string
  userName: string
  email: string
  displayName?: string
  bio?: string
  hasProfilePicture: boolean
}

export interface AuthResponse {
  success: boolean
  user?: AuthUser
  error?: string
}

// ============================================================================
//  Configuration
// ============================================================================

const CLIENT_ID = import.meta.env.VITE_ABP_CLIENT_ID || 'VibeResearching_React'
const CLIENT_SECRET = '' // Not needed for public clients or cookie mode
const SCOPE = 'openid profile email roles offline_access VibeResearching'

// Auth Mode: 'cookie' (new backend) or 'token' (legacy OpenIddict)
const AUTH_MODE = (import.meta.env.VITE_AUTH_MODE || 'cookie') as 'cookie' | 'token'

// Mock mode for development when backend unavailable
const MOCK_ENABLED = import.meta.env.VITE_MOCK_AUTH === 'true'

const MOCK_USERS: Record<string, { password: string; user: AuthUser }> = {
  'admin@sisyphus.ai': {
    password: 'Admin@123',
    user: {
      id: 'mock-admin-001',
      userName: 'admin',
      email: 'admin@sisyphus.ai',
      name: 'System',
      surname: 'Admin',
      roles: ['admin', 'member'],
      isAdmin: true,
      avatarUrl: undefined,
    },
  },
  admin: {
    password: '1q2w3E*',
    user: {
      id: 'mock-admin-002',
      userName: 'admin',
      email: 'admin@localhost',
      name: 'Admin',
      surname: 'Default',
      roles: ['admin', 'member'],
      isAdmin: true,
      avatarUrl: undefined,
    },
  },
}

function mockLogin(input: LoginInput): AuthResponse {
  const mockUser = MOCK_USERS[input.email]
  if (mockUser && mockUser.password === input.password) {
    const mockToken = btoa(JSON.stringify({ sub: mockUser.user.id, exp: Date.now() + 3600000 }))
    setAccessToken(mockToken, 3600)
    console.info('[Auth] Mock login successful for:', input.email)
    return { success: true, user: mockUser.user }
  }
  return { success: false, error: 'Invalid email or password (mock mode)' }
}

// ============================================================================
//  Login - Hybrid Mode (Cookie or Token)
// ============================================================================

export async function abpLogin(input: LoginInput): Promise<AuthResponse> {
  try {
    if (AUTH_MODE === 'cookie') {
      return await cookieLogin(input)
    } else {
      return await tokenLogin(input)
    }
  } catch (error) {
    console.error('[Auth] Login error:', error)

    // Fallback to mock mode if backend unreachable
    if (MOCK_ENABLED && error instanceof TypeError && error.message.includes('Failed to fetch')) {
      console.warn('[Auth] Backend unreachable, falling back to mock login')
      return mockLogin(input)
    }

    return {
      success: false,
      error: error instanceof Error ? error.message : 'An unexpected error occurred.',
    }
  }
}

// ============================================================================
//  Cookie-based Login (New Backend API)
// ============================================================================

async function cookieLogin(input: LoginInput): Promise<AuthResponse> {
  const response = await fetch(`${ABP_API_BASE}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include', // Important: include cookies
    body: JSON.stringify({
      userNameOrEmail: input.email,
      password: input.password,
      rememberMe: input.rememberMe ?? false,
    }),
  })

  if (!response.ok) {
    const data = await response.json().catch(() => ({}))
    const errorMessage = data.error || getErrorMessageByStatus(response.status)
    return { success: false, error: errorMessage }
  }

  // Fetch user profile after successful login
  const user = await fetchCurrentUser()
  if (!user) {
    return { success: false, error: 'Failed to fetch user profile.' }
  }

  // Load permissions after successful login
  await usePermissionStore.getState().loadPermissions()

  return { success: true, user }
}

function getErrorMessageByStatus(status: number): string {
  switch (status) {
    case 401:
      return 'Invalid username or password.'
    case 403:
      return 'Login not allowed. Please verify your email.'
    case 423:
      return 'Account is locked. Please try again later.'
    default:
      return 'Login failed. Please try again.'
  }
}

// ============================================================================
//  Token-based Login (OpenIddict Password Grant - Legacy)
// ============================================================================

async function tokenLogin(input: LoginInput): Promise<AuthResponse> {
  const formData = new URLSearchParams()
  formData.append('grant_type', 'password')
  formData.append('client_id', CLIENT_ID)
  if (CLIENT_SECRET) {
    formData.append('client_secret', CLIENT_SECRET)
  }
  formData.append('username', input.email)
  formData.append('password', input.password)
  formData.append('scope', SCOPE)

  const tokenResponse = await fetch(`${ABP_API_BASE}/connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: formData.toString(),
  })

  if (!tokenResponse.ok) {
    const errorText = await tokenResponse.text()
    let errorMessage = 'The email or password you entered is incorrect.'

    try {
      const errorData = JSON.parse(errorText)
      if (errorData.error_description) {
        errorMessage = errorData.error_description
      } else if (errorData.error) {
        errorMessage = errorData.error
      }
    } catch {
      // Use default error message
    }

    return { success: false, error: errorMessage }
  }

  const tokenData: TokenResponse = await tokenResponse.json()

  setAccessToken(tokenData.access_token, tokenData.expires_in)
  if (tokenData.refresh_token) {
    setRefreshToken(tokenData.refresh_token)
  }

  const user = await fetchCurrentUser()
  if (!user) {
    return { success: false, error: 'Failed to fetch user profile.' }
  }

  // Load permissions after successful token login
  await usePermissionStore.getState().loadPermissions()

  return { success: true, user }
}

// ============================================================================
//  Register - Create Account
// ============================================================================

interface AbpRegisterInput {
  userName: string
  emailAddress: string
  password: string
  appName: string
}

export async function abpRegister(input: RegisterInput): Promise<AuthResponse> {
  try {
    const registerData: AbpRegisterInput = {
      userName: input.userName,
      emailAddress: input.email,
      password: input.password,
      appName: 'VibeResearching',
    }

    await abpFetch<{ tenantId: string | null }>('/api/account/register', {
      method: 'POST',
      body: JSON.stringify(registerData),
      skipAuth: true,
    })

    // Auto-login after registration
    return abpLogin({ email: input.email, password: input.password })
  } catch (error) {
    console.error('[Auth] Register error:', error)

    let errorMessage = 'Registration failed.'
    if (error instanceof Error) {
      if (error.message.includes('already taken')) {
        errorMessage = 'An account with this email or username already exists.'
      } else if (error.message.includes('password')) {
        errorMessage = error.message
      } else {
        errorMessage = error.message
      }
    }

    return { success: false, error: errorMessage }
  }
}

// ============================================================================
//  Fetch Current User
// ============================================================================

export async function fetchCurrentUser(): Promise<AuthUser | null> {
  try {
    // Try new Vibe profile endpoint first
    const profile = await fetchVibeProfile()
    if (profile) return profile

    // Fallback to token-based profile
    const token = getAccessToken()
    if (!token) return null

    return await fetchTokenBasedProfile(token)
  } catch (error) {
    console.error('[Auth] Fetch current user error:', error)
    return null
  }
}

async function fetchVibeProfile(): Promise<AuthUser | null> {
  try {
    const profile = await abpFetch<VibeProfileResponse>('/api/vibe/my-profile', {
      credentials: 'include',
    } as RequestInit)

    const permissions = await fetchMyPermissions()
    // Store permissions in the permission store
    usePermissionStore.setState({ permissions, isLoaded: true })
    const roles = extractRolesFromPermissions(permissions)

    // Parse display name, keep empty if not provided
    const displayName = profile.displayName?.trim() || ''
    const nameParts = displayName.split(' ')
    const firstName = nameParts[0] || ''
    const lastName = nameParts.slice(1).join(' ') || ''

    return {
      id: profile.id,
      userName: profile.userName,
      email: profile.email,
      name: firstName,
      surname: lastName,
      roles,
      isAdmin: roles.includes('admin'),
      avatarUrl: profile.hasProfilePicture
        ? `${ABP_API_BASE}/api/account/profile-picture/${profile.id}`
        : undefined,
    }
  } catch {
    return null
  }
}

async function fetchTokenBasedProfile(token: string): Promise<AuthUser | null> {
  try {
    const response = await fetch(`${ABP_API_BASE}/connect/userinfo`, {
      headers: { Authorization: `Bearer ${token}` },
    })

    if (!response.ok) return null

    const userInfo = await response.json()
    const roles = userInfo.role
      ? Array.isArray(userInfo.role)
        ? userInfo.role
        : [userInfo.role]
      : ['member']

    return {
      id: userInfo.sub || 'current',
      userName: userInfo.name || userInfo.preferred_username,
      email: userInfo.email,
      name: userInfo.given_name,
      surname: userInfo.family_name,
      roles,
      isAdmin: roles.includes('admin'),
      avatarUrl: undefined,
    }
  } catch {
    return null
  }
}

// ============================================================================
//  Fetch My Permissions (New Backend API)
// ============================================================================

export async function fetchMyPermissions(): Promise<string[]> {
  try {
    const result = await abpFetch<{ permissions: string[] }>('/api/account/my-permissions', {
      credentials: 'include',
    } as RequestInit)
    return result.permissions
  } catch {
    return []
  }
}

function extractRolesFromPermissions(permissions: string[]): string[] {
  // Infer roles from permission patterns
  const roles: string[] = ['member']

  if (permissions.some((p) => p.includes('.Admin') || p.includes('AbpIdentity'))) {
    roles.push('admin')
  }

  return [...new Set(roles)]
}

// ============================================================================
//  Logout
// ============================================================================

export async function abpLogout(): Promise<void> {
  try {
    if (AUTH_MODE === 'cookie') {
      // Cookie-based logout
      await fetch(`${ABP_API_BASE}/api/auth/logout`, {
        method: 'POST',
        credentials: 'include',
      }).catch(() => {})
    } else {
      // Token revocation
      const token = getAccessToken()
      if (token) {
        const formData = new URLSearchParams()
        formData.append('token', token)
        formData.append('client_id', CLIENT_ID)
        if (CLIENT_SECRET) {
          formData.append('client_secret', CLIENT_SECRET)
        }

        await fetch(`${ABP_API_BASE}/connect/revocation`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: formData.toString(),
        }).catch(() => {})
      }
    }
  } finally {
    clearTokens()
    usePermissionStore.getState().clearPermissions()
  }
}

// ============================================================================
//  Refresh Token (Token mode only)
// ============================================================================

export async function abpRefreshToken(): Promise<boolean> {
  if (AUTH_MODE === 'cookie') {
    // Cookie-based auth doesn't need manual refresh
    return true
  }

  try {
    const refreshToken = getRefreshToken()
    if (!refreshToken) return false

    const formData = new URLSearchParams()
    formData.append('grant_type', 'refresh_token')
    formData.append('client_id', CLIENT_ID)
    if (CLIENT_SECRET) {
      formData.append('client_secret', CLIENT_SECRET)
    }
    formData.append('refresh_token', refreshToken)

    const response = await fetch(`${ABP_API_BASE}/connect/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: formData.toString(),
    })

    if (!response.ok) {
      clearTokens()
      return false
    }

    const tokenData: TokenResponse = await response.json()
    setAccessToken(tokenData.access_token, tokenData.expires_in)
    if (tokenData.refresh_token) {
      setRefreshToken(tokenData.refresh_token)
    }

    return true
  } catch {
    clearTokens()
    return false
  }
}

// ============================================================================
//  Password Reset
// ============================================================================

export async function abpForgotPassword(email: string): Promise<{ success: boolean; error?: string }> {
  try {
    await abpFetch('/api/account/send-password-reset-code', {
      method: 'POST',
      body: JSON.stringify({
        email,
        appName: 'VibeResearching',
        returnUrl: `${window.location.origin}/reset-password`,
      }),
      skipAuth: true,
    })

    return { success: true }
  } catch (error) {
    console.error('[Auth] Forgot password error:', error)
    
    // Show network/server errors to user for debugging
    // But hide "user not found" errors to prevent email enumeration
    if (error instanceof TypeError && error.message.includes('Failed to fetch')) {
      return { success: false, error: 'Unable to connect to server. Please try again later.' }
    }
    if (error instanceof Error && error.message.includes('500')) {
      return { success: false, error: 'Server error. Please try again later.' }
    }
    
    // For other errors (like 404 user not found), return success to prevent email enumeration
    return { success: true }
  }
}

export async function abpResetPassword(
  userId: string,
  resetToken: string,
  password: string
): Promise<{ success: boolean; error?: string }> {
  try {
    await abpFetch('/api/account/reset-password', {
      method: 'POST',
      body: JSON.stringify({ userId, resetToken, password }),
      skipAuth: true,
    })

    return { success: true }
  } catch (error) {
    console.error('[Auth] Reset password error:', error)
    return {
      success: false,
      error: error instanceof Error ? error.message : 'Password reset failed.',
    }
  }
}

// ============================================================================
//  Email Verification
// ============================================================================

export async function abpVerifyEmail(
  userId: string,
  confirmationCode: string
): Promise<{ success: boolean; error?: string }> {
  try {
    await abpFetch('/api/account/confirm-email', {
      method: 'POST',
      body: JSON.stringify({ userId, confirmationCode }),
      skipAuth: true,
    })

    return { success: true }
  } catch (error) {
    console.error('[Auth] Verify email error:', error)
    return {
      success: false,
      error: error instanceof Error ? error.message : 'Email verification failed.',
    }
  }
}

export async function abpResendVerification(email: string): Promise<{ success: boolean; error?: string }> {
  try {
    await abpFetch('/api/account/send-email-confirmation-link', {
      method: 'POST',
      body: JSON.stringify({
        email,
        appName: 'VibeResearching',
        returnUrl: `${window.location.origin}/email-confirmation`,
      }),
      skipAuth: true,
    })

    return { success: true }
  } catch (error) {
    console.error('[Auth] Resend verification error:', error)
    return { success: true } // Always return success
  }
}

// ============================================================================
//  Check Auth Status
// ============================================================================

export function isAuthenticated(): boolean {
  if (AUTH_MODE === 'cookie') {
    // For cookie mode, we can't directly check - rely on API calls
    return true // Will be validated on first API call
  }
  return !!getAccessToken()
}
