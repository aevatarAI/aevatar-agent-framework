// ============================================================================
//  ABP Profile API - User Profile & Avatar Management
//  Endpoints: /api/vibe/my-profile, /api/account/profile-picture
// ============================================================================

import { ABP_API_BASE, abpFetch, getAccessToken } from './config'

// Helper to get auth headers for fetch calls
function getAuthHeaders(): HeadersInit {
  const token = getAccessToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

// ============================================================================
//  Types
// ============================================================================

export interface UserProfile {
  id: string
  userName: string
  email: string
  displayName: string
  bio: string
  hasProfilePicture: boolean
  avatarUrl?: string
}

export interface UpdateProfileInput {
  displayName?: string
  bio?: string
}

export interface ChangePasswordInput {
  currentPassword: string
  newPassword: string
}

export interface ProfilePictureResult {
  ok: boolean
  url?: string
  error?: string
}

// ============================================================================
//  Get My Profile
// ============================================================================

export async function getMyProfile(): Promise<UserProfile | null> {
  try {
    const data = await abpFetch<{
      id: string
      userName: string
      email: string
      displayName?: string
      bio?: string
      hasProfilePicture: boolean
    }>('/api/vibe/my-profile', {
      credentials: 'include',
    } as RequestInit)

    return {
      id: data.id,
      userName: data.userName,
      email: data.email,
      displayName: data.displayName || '',
      bio: data.bio || '',
      hasProfilePicture: data.hasProfilePicture,
      avatarUrl: data.hasProfilePicture
        ? `${ABP_API_BASE}/api/account/profile-picture/${data.id}`
        : undefined,
    }
  } catch (error) {
    console.error('[Profile] Get profile error:', error)
    return null
  }
}

// ============================================================================
//  Update My Profile
// ============================================================================

export async function updateMyProfile(input: UpdateProfileInput): Promise<{ ok: boolean; error?: string }> {
  try {
    await abpFetch('/api/vibe/my-profile', {
      method: 'PUT',
      body: JSON.stringify(input),
      credentials: 'include',
    } as RequestInit)

    return { ok: true }
  } catch (error) {
    console.error('[Profile] Update profile error:', error)
    return {
      ok: false,
      error: error instanceof Error ? error.message : 'Failed to update profile.',
    }
  }
}

// ============================================================================
//  Change Password
// ============================================================================

export async function changePassword(input: ChangePasswordInput): Promise<{
  ok: boolean
  error?: string
  details?: string[]
}> {
  try {
    await abpFetch('/api/vibe/change-password', {
      method: 'POST',
      body: JSON.stringify(input),
      credentials: 'include',
    } as RequestInit)

    return { ok: true }
  } catch (error) {
    console.error('[Profile] Change password error:', error)

    if (error instanceof Error) {
      // Try to extract validation details
      const errorWithDetails = error as Error & { details?: string[] }
      return {
        ok: false,
        error: error.message,
        details: errorWithDetails.details,
      }
    }

    return { ok: false, error: 'Failed to change password.' }
  }
}

// ============================================================================
//  Profile Picture - Upload
// ============================================================================

const MAX_FILE_SIZE = 2 * 1024 * 1024 // 2MB
const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp']

export async function uploadProfilePicture(file: File): Promise<ProfilePictureResult> {
  // Client-side validation
  if (file.size > MAX_FILE_SIZE) {
    return { ok: false, error: 'File too large. Maximum size is 2MB.' }
  }

  if (!ALLOWED_TYPES.includes(file.type)) {
    return { ok: false, error: 'Invalid file type. Allowed: JPG, PNG, WebP.' }
  }

  try {
    const formData = new FormData()
    formData.append('file', file)

    const response = await fetch(`${ABP_API_BASE}/api/account/profile-picture`, {
      method: 'PUT',
      body: formData,
      credentials: 'include',
      headers: getAuthHeaders(), // Support token mode
    })

    if (!response.ok) {
      const data = await response.json().catch(() => ({}))
      return { ok: false, error: data.error || 'Failed to upload profile picture.' }
    }

    const data = await response.json()
    return {
      ok: true,
      url: data.url ? `${ABP_API_BASE}${data.url}` : undefined,
    }
  } catch (error) {
    console.error('[Profile] Upload picture error:', error)
    return {
      ok: false,
      error: error instanceof Error ? error.message : 'Failed to upload profile picture.',
    }
  }
}

// ============================================================================
//  Profile Picture - Get URL
// ============================================================================

export function getProfilePictureUrl(userId: string): string {
  return `${ABP_API_BASE}/api/account/profile-picture/${userId}`
}

// ============================================================================
//  Profile Picture - Delete
// ============================================================================

export async function deleteProfilePicture(): Promise<{ ok: boolean; error?: string }> {
  try {
    const response = await fetch(`${ABP_API_BASE}/api/account/profile-picture`, {
      method: 'DELETE',
      credentials: 'include',
      headers: getAuthHeaders(), // Support token mode
    })

    if (!response.ok) {
      const data = await response.json().catch(() => ({}))
      return { ok: false, error: data.error || 'Failed to delete profile picture.' }
    }

    return { ok: true }
  } catch (error) {
    console.error('[Profile] Delete picture error:', error)
    return {
      ok: false,
      error: error instanceof Error ? error.message : 'Failed to delete profile picture.',
    }
  }
}

// ============================================================================
//  Check if Profile Picture Exists
// ============================================================================

export async function checkProfilePictureExists(userId: string): Promise<boolean> {
  try {
    const response = await fetch(`${ABP_API_BASE}/api/account/profile-picture/${userId}`, {
      method: 'HEAD',
    })
    return response.ok
  } catch {
    return false
  }
}
