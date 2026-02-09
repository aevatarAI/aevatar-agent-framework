// ============================================================
//  OAuth Types - Redirect Flow
// ============================================================
//
//  Both Google and GitHub use redirect flow with backend
//  token exchange for security and ABP Identity integration.
//
// ============================================================

export interface OAuthUser {
  id: string
  email: string
  name: string
  picture?: string
  provider: 'google' | 'github'
}

export interface OAuthResult {
  success: boolean
  user?: OAuthUser
  error?: string
}

// ─────────────────────────────────────────────────────────────────────
// Legacy types (kept for backward compatibility, may be removed)
// ─────────────────────────────────────────────────────────────────────

/** @deprecated No longer used with redirect flow */
export interface GoogleCredentialResponse {
  credential: string
  select_by: string
  clientId: string
}

/** @deprecated No longer used with redirect flow */
export interface GoogleDecodedToken {
  iss: string
  azp: string
  aud: string
  sub: string
  email: string
  email_verified: boolean
  name: string
  picture: string
  given_name: string
  family_name: string
  locale: string
  iat: number
  exp: number
}

/** @deprecated Use backend response instead */
export interface GitHubUser {
  id: number
  login: string
  name: string | null
  email: string | null
  avatar_url: string
}
