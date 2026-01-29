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
    avatarUrl: undefined,
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
  const queryParams: Record<string, unknown> = {
    SkipCount: params?.skip || 0,
    MaxResultCount: params?.take || 10,
    Sorting: 'creationTime desc',
  }

  if (params?.search) {
    queryParams.Filter = params.search
  }

  const queryString = buildQueryString(queryParams)
  const result = await abpFetch<AbpPagedResult<AbpIdentityUserDto>>(
    `/api/identity/users${queryString}`
  )

  // Fetch roles for each user
  const usersWithRoles = await Promise.all(
    result.items.map(async (abpUser) => {
      const roles = await getUserRoles(abpUser.id)
      return transformAbpUserToUser(abpUser, roles)
    })
  )

  // Apply client-side filters (ABP doesn't support role/status filter in API)
  let filtered = usersWithRoles

  if (params?.role) {
    filtered = filtered.filter((u) => u.roles.includes(params.role!))
  }

  if (params?.status) {
    filtered = filtered.filter((u) =>
      params.status === 'active' ? u.isActive : !u.isActive
    )
  }

  return {
    items: filtered,
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
//  Get User Statistics
// ============================================================================

export async function getUserStats(): Promise<{
  total: number
  active: number
  roles: number
  inactive: number
}> {
  // Fetch all users to calculate stats (ABP doesn't have a stats endpoint)
  const result = await abpFetch<AbpPagedResult<AbpIdentityUserDto>>(
    '/api/identity/users?MaxResultCount=1000'
  )

  const users = result.items
  const activeCount = users.filter((u) => u.isActive).length

  // Get unique roles count
  const rolesResult = await abpFetch<AbpPagedResult<AbpIdentityRoleDto>>(
    '/api/identity/roles'
  )

  return {
    total: result.totalCount,
    active: activeCount,
    roles: rolesResult.totalCount,
    inactive: result.totalCount - activeCount,
  }
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
