// ============================================================
//  User Management Types - ABP Framework Aligned
// ============================================================

// === User Types ===
export interface User {
  id: string
  userName: string
  email: string
  name?: string
  surname?: string
  phoneNumber?: string
  isActive: boolean
  lockoutEnabled: boolean
  lockoutEnd?: string
  emailConfirmed: boolean
  twoFactorEnabled: boolean
  createdAt: string
  lastLoginTime?: string
  roles: string[]
  avatarUrl?: string
}

export interface CreateUserInput {
  userName: string
  email: string
  name?: string
  surname?: string
  phoneNumber?: string
  password: string
  isActive: boolean
  lockoutEnabled: boolean
  roleNames: string[]
}

export interface UpdateUserInput {
  userName: string
  email: string
  name?: string
  surname?: string
  phoneNumber?: string
  isActive: boolean
  lockoutEnabled: boolean
  roleNames: string[]
}

// === Role Types ===
export interface Role {
  id: string
  name: string
  description?: string
  isDefault: boolean
  isStatic: boolean
  isPublic: boolean
  userCount: number
  permissionCount: number
}

export interface CreateRoleInput {
  name: string
  isDefault: boolean
  isPublic: boolean
}

export interface UpdateRoleInput {
  name: string
  isDefault: boolean
  isPublic: boolean
}

// === Permission Types ===
export interface PermissionGrantSource {
  providerName: PermissionProviderName
  providerKey: string
}

export interface Permission {
  name: string
  displayName: string
  parentName?: string
  isGranted: boolean
  allowedProviders: PermissionProviderName[]
  // Source of permission grants (from roles, user direct, etc.)
  // Used to show inherited permissions in User Permissions view
  grantedProviders?: PermissionGrantSource[]
}

export interface PermissionGroup {
  name: string
  displayName: string
  permissions: Permission[]
}

export interface PermissionGrantInfo {
  providerName: PermissionProviderName
  providerKey: string
  permissions: {
    name: string
    isGranted: boolean
  }[]
}

// ABP Permission Provider configuration
export type PermissionProviderName = "R" | "U" | "C"

export interface PermissionProvider {
  name: PermissionProviderName
  displayName: string
  description: string
  icon: "shield" | "user" | "monitor"
  isEnabled: boolean
}

// === Auth Types ===
export interface LoginInput {
  email: string
  password: string
  rememberMe?: boolean
}

export interface RegisterInput {
  userName: string
  email: string
  password: string
  name?: string
  surname?: string
}

export interface AuthUser {
  id: string
  userName: string
  email: string
  name?: string
  surname?: string
  phoneNumber?: string
  roles: string[]
  isAdmin: boolean
  avatarUrl?: string
  loginProvider?: 'local' | 'Google' | 'GitHub'  // OAuth users have no password
}

export interface AuthState {
  user: AuthUser | null
  isAuthenticated: boolean
  isLoading: boolean
}

// === Account Types ===
export interface ChangePasswordInput {
  currentPassword: string
  newPassword: string
}

export interface ProfileInput {
  userName: string
  email: string
  name?: string
  surname?: string
  phoneNumber?: string
}

export interface TwoFactorState {
  isEnabled: boolean
  authenticatorKey?: string
  recoveryCodes?: string[]
}

export interface SecurityLog {
  id: string
  action: string
  browserInfo: string
  clientIpAddress: string
  createdAt: string
  identity?: string
}

export interface UserSession {
  id: string
  deviceInfo: string
  ipAddress: string
  lastAccessTime: string
  isCurrent: boolean
}

export interface LinkedAccount {
  provider: string
  providerDisplayName: string
  isConnected: boolean
  connectedAt?: string
}

export interface NotificationPreferences {
  emailNotifications: boolean
  pushNotifications: boolean
  securityAlerts: boolean
  systemUpdates: boolean
  researchUpdates: boolean
}

// === API Response Types ===
export interface PagedResult<T> {
  items: T[]
  totalCount: number
}

export interface ApiError {
  code: string
  message: string
  details?: string
}
