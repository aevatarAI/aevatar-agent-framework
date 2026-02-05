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
//  Get Users (Paginated)
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
  const hasStatusFilter = !!params?.status
  const hasRoleFilter = !!params?.role

  // Build query params
  const queryParams: Record<string, unknown> = {
    Sorting: 'creationTime desc',
  }

  if (params?.search) {
    queryParams.Filter = params.search
  }

  // ============================================================
  // Strategy: Minimize API calls by filtering before fetching roles
  // ============================================================

  // Case 1: No filters - use server pagination, fetch roles only for current page
  if (!hasStatusFilter && !hasRoleFilter) {
    queryParams.SkipCount = skip
    queryParams.MaxResultCount = take

    const queryString = buildQueryString(queryParams)
    const result = await abpFetch<AbpPagedResult<AbpIdentityUserDto>>(
      `/api/identity/users${queryString}`
    )

    // Only fetch roles for the current page (10 users max)
    const usersWithRoles = await Promise.all(
      result.items.map(async (abpUser) => {
        const roles = await getUserRoles(abpUser.id)
        return transformAbpUserToUser(abpUser, roles)
      })
    )

    return {
      items: usersWithRoles,
      totalCount: result.totalCount,
    }
  }

  // Case 2: Status filter only - filter first, then fetch roles for result page only
  if (hasStatusFilter && !hasRoleFilter) {
    queryParams.SkipCount = 0
    queryParams.MaxResultCount = 1000

    const queryString = buildQueryString(queryParams)
    const result = await abpFetch<AbpPagedResult<AbpIdentityUserDto>>(
      `/api/identity/users${queryString}`
    )

    // Filter by status BEFORE fetching roles (no role API calls yet!)
    const statusFiltered = result.items.filter((u) =>
      params.status === 'active' ? u.isActive : !u.isActive
    )

    // Paginate the filtered results
    const paginatedItems = statusFiltered.slice(skip, skip + take)

    // Only fetch roles for the paginated items (10 users max)
    const usersWithRoles = await Promise.all(
      paginatedItems.map(async (abpUser) => {
        const roles = await getUserRoles(abpUser.id)
        return transformAbpUserToUser(abpUser, roles)
      })
    )

    return {
      items: usersWithRoles,
      totalCount: statusFiltered.length,
    }
  }

  // Case 3: Role filter (with or without status) - must fetch roles to filter
  // Optimization: Fetch roles in batches, filter, then paginate
  queryParams.SkipCount = 0
  queryParams.MaxResultCount = 1000

  const queryString = buildQueryString(queryParams)
  const result = await abpFetch<AbpPagedResult<AbpIdentityUserDto>>(
    `/api/identity/users${queryString}`
  )

  // Pre-filter by status if applicable (reduces role API calls)
  let candidates = result.items
  if (hasStatusFilter) {
    candidates = candidates.filter((u) =>
      params.status === 'active' ? u.isActive : !u.isActive
    )
  }

  // Fetch roles for candidates and filter by role
  const usersWithRoles = await Promise.all(
    candidates.map(async (abpUser) => {
      const roles = await getUserRoles(abpUser.id)
      return transformAbpUserToUser(abpUser, roles)
    })
  )

  // Filter by role
  const roleFiltered = usersWithRoles.filter((u) => u.roles.includes(params.role!))

  // Paginate
  const paginatedItems = roleFiltered.slice(skip, skip + take)

  return {
    items: paginatedItems,
    totalCount: roleFiltered.length,
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
//  Get User Statistics (with caching)
// ============================================================================

// Cache for user stats to avoid repeated expensive API calls
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

  // Fetch users (without roles - just for counting)
  const result = await abpFetch<AbpPagedResult<AbpIdentityUserDto>>(
    '/api/identity/users?MaxResultCount=1000'
  )

  const users = result.items
  const activeCount = users.filter((u) => u.isActive).length

  // Get unique roles count
  const rolesResult = await abpFetch<AbpPagedResult<AbpIdentityRoleDto>>(
    '/api/identity/roles'
  )

  // For admin count: Only check a small sample (10 users) to estimate
  // or use cached value if available
  let adminCount = statsCache.data?.admins || 0
  
  // Only recalculate admin count if cache is completely empty
  if (!statsCache.data) {
    // Check only first 10 users to get admin count quickly
    const sampleSize = Math.min(10, users.length)
    const samplePromises = users.slice(0, sampleSize).map(async (user) => {
      const roles = await getUserRoles(user.id)
      return roles.includes('admin')
    })
    const sampleResults = await Promise.all(samplePromises)
    const sampleAdminCount = sampleResults.filter(Boolean).length
    
    // Estimate total admin count based on sample ratio
    // Or just use the sample count as a minimum
    adminCount = sampleAdminCount
  }

  const stats = {
    total: result.totalCount,
    active: activeCount,
    roles: rolesResult.totalCount,
    inactive: result.totalCount - activeCount,
    admins: adminCount,
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
