// ============================================================================
//  ABP Users API - Identity User Management
// ============================================================================

import type { User, CreateUserInput, UpdateUserInput, PagedResult } from '@/types/user-management'
import { abpFetch, buildQueryString, type AbpPagedResult } from './config'

// === ABP Identity User DTO ===

interface AbpIdentityUserDto {
  id: string
  tenantId?: string
  userName: string
  name?: string
  surname?: string
  email: string
  emailConfirmed: boolean
  phoneNumber?: string
  phoneNumberConfirmed: boolean
  isActive: boolean
  lockoutEnabled: boolean
  lockoutEnd?: string
  concurrencyStamp: string
  isDeleted: boolean
  deleterId?: string
  deletionTime?: string
  lastModificationTime?: string
  lastModifierId?: string
  creationTime: string
  creatorId?: string
  extraProperties: Record<string, unknown>
}

interface AbpIdentityUserCreateDto {
  userName: string
  name?: string
  surname?: string
  email: string
  phoneNumber?: string
  isActive: boolean
  lockoutEnabled: boolean
  roleNames: string[]
  password: string
}

interface AbpIdentityUserUpdateDto {
  userName: string
  name?: string
  surname?: string
  email: string
  phoneNumber?: string
  isActive: boolean
  lockoutEnabled: boolean
  roleNames: string[]
  concurrencyStamp?: string
}

interface AbpIdentityRoleDto {
  id: string
  name: string
  isDefault: boolean
  isStatic: boolean
  isPublic: boolean
}

// ============================================================================
//  Transform Functions
// ============================================================================

function transformAbpUserToUser(abpUser: AbpIdentityUserDto, roles: string[] = []): User {
  return {
    id: abpUser.id,
    userName: abpUser.userName,
    email: abpUser.email,
    name: abpUser.name,
    surname: abpUser.surname,
    phoneNumber: abpUser.phoneNumber,
    isActive: abpUser.isActive,
    lockoutEnabled: abpUser.lockoutEnabled,
    lockoutEnd: abpUser.lockoutEnd,
    emailConfirmed: abpUser.emailConfirmed,
    twoFactorEnabled: false, // ABP requires separate call
    createdAt: abpUser.creationTime,
    lastLoginTime: undefined, // ABP doesn't expose this directly
    roles,
    // ABP profile picture URL - Avatar component will fallback to initials if 404
    avatarUrl: `/api/account/profile-picture/${abpUser.id}`,
  }
}

// ============================================================================
//  Vibe User API Response Type
// ============================================================================

interface VibeUserDto {
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
  creationTime: string
  roles: string[]
}

interface VibeUsersResult {
  items: VibeUserDto[]
  totalCount: number
}

// ============================================================================
//  Get Users (Paginated) - Using optimized backend API
// ============================================================================

export async function getUsers(params?: {
  skip?: number
  take?: number
  search?: string
  role?: string
  status?: 'active' | 'inactive'
}): Promise<PagedResult<User>> {
  const skip = params?.skip || 0
  const take = params?.take || 10

  // Build query params for the new optimized API
  const queryParams: Record<string, unknown> = {
    skipCount: skip,
    maxResultCount: take,
  }

  if (params?.search) {
    queryParams.filter = params.search
  }

  if (params?.role) {
    queryParams.roleName = params.role
  }

  if (params?.status) {
    queryParams.status = params.status
  }

  const queryString = buildQueryString(queryParams)

  // Use the new optimized backend API that handles filtering server-side
  const result = await abpFetch<VibeUsersResult>(
    `/api/vibe/users${queryString}`
  )

  // Transform to frontend User type
  const users: User[] = result.items.map((user) => ({
    id: user.id,
    userName: user.userName,
    email: user.email,
    name: user.name,
    surname: user.surname,
    phoneNumber: user.phoneNumber,
    isActive: user.isActive,
    lockoutEnabled: user.lockoutEnabled,
    lockoutEnd: user.lockoutEnd,
    emailConfirmed: user.emailConfirmed,
    twoFactorEnabled: false,
    createdAt: user.creationTime,
    lastLoginTime: undefined,
    roles: user.roles,
    avatarUrl: `/api/account/profile-picture/${user.id}`,
  }))

  return {
    items: users,
    totalCount: result.totalCount,
  }
}

// ============================================================================
//  Get Single User
// ============================================================================

export async function getUser(id: string): Promise<User | null> {
  try {
    const abpUser = await abpFetch<AbpIdentityUserDto>(`/api/identity/users/${id}`)
    const roles = await getUserRoles(id)
    return transformAbpUserToUser(abpUser, roles)
  } catch (error) {
    console.error('[ABP Users] Get user error:', error)
    return null
  }
}

// ============================================================================
//  Get User Roles
// ============================================================================

export async function getUserRoles(userId: string): Promise<string[]> {
  try {
    const result = await abpFetch<{ items: AbpIdentityRoleDto[] }>(
      `/api/identity/users/${userId}/roles`
    )
    return result.items.map((r) => r.name)
  } catch {
    return []
  }
}

// ============================================================================
//  Create User
// ============================================================================

export async function createUser(input: CreateUserInput): Promise<User> {
  const createDto: AbpIdentityUserCreateDto = {
    userName: input.userName,
    email: input.email,
    name: input.name,
    surname: input.surname,
    phoneNumber: input.phoneNumber,
    isActive: input.isActive,
    lockoutEnabled: input.lockoutEnabled,
    roleNames: input.roleNames,
    password: input.password,
  }

  const abpUser = await abpFetch<AbpIdentityUserDto>('/api/identity/users', {
    method: 'POST',
    body: JSON.stringify(createDto),
  })

  return transformAbpUserToUser(abpUser, input.roleNames)
}

// ============================================================================
//  Update User
// ============================================================================

export async function updateUser(id: string, input: UpdateUserInput): Promise<User> {
  // First, get current user to obtain concurrencyStamp
  const currentUser = await abpFetch<AbpIdentityUserDto>(`/api/identity/users/${id}`)

  const updateDto: AbpIdentityUserUpdateDto = {
    userName: input.userName,
    email: input.email,
    name: input.name,
    surname: input.surname,
    phoneNumber: input.phoneNumber,
    isActive: input.isActive,
    lockoutEnabled: input.lockoutEnabled,
    roleNames: input.roleNames,
    concurrencyStamp: currentUser.concurrencyStamp,
  }

  const abpUser = await abpFetch<AbpIdentityUserDto>(`/api/identity/users/${id}`, {
    method: 'PUT',
    body: JSON.stringify(updateDto),
  })

  return transformAbpUserToUser(abpUser, input.roleNames)
}

// ============================================================================
//  Delete User
// ============================================================================

export async function deleteUser(id: string): Promise<void> {
  await abpFetch(`/api/identity/users/${id}`, {
    method: 'DELETE',
  })
}

// ============================================================================
//  Set User Password (Admin reset)
// ============================================================================

export async function setUserPassword(id: string, password: string): Promise<void> {
  // Get current user data first
  const currentUser = await abpFetch<AbpIdentityUserDto>(`/api/identity/users/${id}`)
  const roles = await getUserRoles(id)

  // Update user with new password
  // ABP requires sending all user data when updating, including password for reset
  await abpFetch(`/api/identity/users/${id}`, {
    method: 'PUT',
    body: JSON.stringify({
      userName: currentUser.userName,
      email: currentUser.email,
      name: currentUser.name,
      surname: currentUser.surname,
      phoneNumber: currentUser.phoneNumber,
      isActive: currentUser.isActive,
      lockoutEnabled: currentUser.lockoutEnabled,
      roleNames: roles,
      password: password, // Set new password
      concurrencyStamp: currentUser.concurrencyStamp,
    }),
  })
}

// ============================================================================
//  Toggle User Lock
// ============================================================================

export async function toggleUserLock(id: string, locked: boolean): Promise<User> {
  // Get current user
  const currentUser = await abpFetch<AbpIdentityUserDto>(`/api/identity/users/${id}`)
  const roles = await getUserRoles(id)

  // Update lockout status
  const updateDto: AbpIdentityUserUpdateDto = {
    userName: currentUser.userName,
    email: currentUser.email,
    name: currentUser.name,
    surname: currentUser.surname,
    phoneNumber: currentUser.phoneNumber,
    isActive: currentUser.isActive,
    lockoutEnabled: locked,
    roleNames: roles,
    concurrencyStamp: currentUser.concurrencyStamp,
  }

  const abpUser = await abpFetch<AbpIdentityUserDto>(`/api/identity/users/${id}`, {
    method: 'PUT',
    body: JSON.stringify(updateDto),
  })

  return transformAbpUserToUser(abpUser, roles)
}

// ============================================================================
//  Vibe User Stats API Response Type
// ============================================================================

interface VibeUserStatsResult {
  totalUsers: number
  activeUsers: number
  inactiveUsers: number
  totalRoles: number
  roleStats: Array<{
    roleName: string
    userCount: number
  }>
}

// ============================================================================
//  Get User Statistics - Using optimized backend API
// ============================================================================

// Cache for user stats to avoid repeated API calls
let statsCache: {
  data: { total: number; active: number; roles: number; inactive: number; admins: number } | null
  timestamp: number
} = { data: null, timestamp: 0 }

const STATS_CACHE_TTL = 30000 // 30 seconds

export async function getUserStats(): Promise<{
  total: number
  active: number
  roles: number
  inactive: number
  admins: number
}> {
  // Return cached data if still valid
  const now = Date.now()
  if (statsCache.data && (now - statsCache.timestamp) < STATS_CACHE_TTL) {
    return statsCache.data
  }

  // Use the new optimized backend API
  const result = await abpFetch<VibeUserStatsResult>('/api/vibe/user-stats')

  // Find admin count from role stats
  const adminRoleStat = result.roleStats.find(
    (r) => r.roleName.toLowerCase() === 'admin'
  )

  const stats = {
    total: result.totalUsers,
    active: result.activeUsers,
    roles: result.totalRoles,
    inactive: result.inactiveUsers,
    admins: adminRoleStat?.userCount || 0,
  }

  // Update cache
  statsCache = { data: stats, timestamp: now }

  return stats
}

// Force refresh stats cache (call after user create/delete)
export function invalidateStatsCache(): void {
  statsCache = { data: null, timestamp: 0 }
}

// ============================================================================
//  Get Assignable Roles
// ============================================================================

export async function getAssignableRoles(): Promise<string[]> {
  const result = await abpFetch<AbpPagedResult<AbpIdentityRoleDto>>(
    '/api/identity/users/assignable-roles'
  )
  return result.items.map((r) => r.name)
}
