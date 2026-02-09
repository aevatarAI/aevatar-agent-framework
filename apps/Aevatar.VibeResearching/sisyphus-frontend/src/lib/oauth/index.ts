// ============================================================
//  OAuth Module - Unified Export
// ============================================================
//
//  Both Google and GitHub use redirect flow with backend
//  token exchange for security and ABP Identity integration.
//
// ============================================================

export * from './config'
export * from './types'

// Google OAuth (redirect flow)
export {
  initiateGoogleLogin,
  handleGoogleCallback,
  isGoogleCallback,
  getGoogleCallbackParams,
  mockGoogleLogin,
  // Legacy exports (deprecated)
  signInWithGoogle,
  loadGoogleSdk,
  initializeGoogleAuth,
  renderGoogleButton,
  signOutGoogle,
} from './google'

// GitHub OAuth (redirect flow)
export {
  initiateGitHubLogin,
  handleGitHubCallback,
  isGitHubCallback,
  getGitHubCallbackParams,
  mockGitHubLogin,
} from './github'
