// ============================================================
//  OAuth Configuration - Environment Variables
// ============================================================
//
//  Both Google and GitHub use the redirect flow with backend
//  token exchange for security and ABP Identity integration.
//
// ============================================================

export const oauthConfig = {
  google: {
    clientId: import.meta.env.VITE_GOOGLE_CLIENT_ID || '',
    redirectUri: `${window.location.origin}/auth/callback/google`,
    scope: 'openid email profile',
    authUrl: 'https://accounts.google.com/o/oauth2/v2/auth',
  },
  github: {
    clientId: import.meta.env.VITE_GITHUB_CLIENT_ID || '',
    redirectUri: `${window.location.origin}/auth/callback/github`,
    scope: 'read:user user:email',
    authUrl: 'https://github.com/login/oauth/authorize',
  },
  features: {
    enableOAuth: import.meta.env.VITE_ENABLE_OAUTH === 'true',
    enableMockAuth: import.meta.env.VITE_ENABLE_MOCK_AUTH === 'true',
  },
} as const

// Check if OAuth providers are configured
export const isGoogleConfigured = () => Boolean(oauthConfig.google.clientId)
export const isGitHubConfigured = () => Boolean(oauthConfig.github.clientId)
export const isOAuthEnabled = () => oauthConfig.features.enableOAuth
