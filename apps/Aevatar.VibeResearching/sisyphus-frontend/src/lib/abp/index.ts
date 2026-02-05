// ============================================================================
//  ABP API Client - Unified Re-exports
//  All modules accessible from '@/lib/abp'
// ============================================================================

// === Configuration ===
export {
  ABP_API_BASE,
  TOKEN_KEY,
  REFRESH_TOKEN_KEY,
  getAccessToken,
  setAccessToken,
  getRefreshToken,
  setRefreshToken,
  clearTokens,
  isTokenExpired,
  abpFetch,
  buildQueryString,
  type AbpErrorResponse,
  type AbpPagedResult,
  type AbpFetchOptions,
} from './config'

// === Auth APIs ===
export {
  abpLogin,
  abpRegister,
  abpLogout,
  abpRefreshToken,
  abpForgotPassword,
  abpResetPassword,
  abpVerifyEmail,
  abpResendVerification,
  fetchCurrentUser,
  fetchMyPermissions,
  isAuthenticated,
  type AuthResponse,
} from './auth'

// === Profile APIs (New) ===
export {
  getMyProfile,
  updateMyProfile,
  changePassword,
  uploadProfilePicture,
  getProfilePictureUrl,
  deleteProfilePicture,
  checkProfilePictureExists,
  type UserProfile,
  type UpdateProfileInput,
  type ChangePasswordInput,
  type ProfilePictureResult,
} from './profile'

// === User Management APIs ===
export {
  getUsers,
  getUser,
  getUserRoles,
  createUser,
  updateUser,
  deleteUser,
  setUserPassword,
  toggleUserLock,
  getUserStats,
  invalidateStatsCache,
  getAssignableRoles,
} from './users'

// === Role Management APIs ===
export {
  getRoles,
  getRole,
  createRole,
  updateRole,
  deleteRole,
  getRolePermissions,
  updateRolePermissions,
} from './roles'

// === Permission Management APIs ===
export {
  getMyPermissions,
  hasPermission,
  hasAnyPermission,
  hasAllPermissions,
  getPermissionProviders,
  getPermissions,
  updatePermissions,
  grantAllPermissions,
  revokeAllPermissions,
  getAllPermissionGroups,
} from './permissions'
