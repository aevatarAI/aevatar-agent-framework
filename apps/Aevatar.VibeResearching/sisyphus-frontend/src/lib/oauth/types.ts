// ============================================================
//  OAuth Types
// ============================================================

export interface OAuthUser {
  id: string
  email: string
  name: string
  picture?: string
  provider: 'google' | 'github'
}

export interface GoogleCredentialResponse {
  credential: string
  select_by: string
  clientId: string
}

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

export interface GitHubUser {
  id: number
  login: string
  name: string | null
  email: string | null
  avatar_url: string
}

export interface OAuthResult {
  success: boolean
  user?: OAuthUser
  error?: string
}
