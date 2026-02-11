// ============================================================================
//  ABP Roles API - Identity Role Management
// ============================================================================

import type { Role, CreateRoleInput, UpdateRoleInput } from '@/types/user-management'
import { abpFetch, type AbpPagedResult } from './config'

// === ABP Identity Role DTO ===

interface AbpIdentityRoleDto {
  id: string
  name: string
  isDefault: boolean
  isStatic: boolean
  isPublic: boolean
  concurrencyStamp: string
  extraProperties: Record<string, unknown>
}

interface AbpIdentityRoleCreateDto {
  name: string
  isDefault: boolean
  isPublic: boolean
}

interface AbpIdentityRoleUpdateDto {
  name: string
  isDefault: boolean
  isPublic: boolean
  concurrencyStamp?: string
}

// ============================================================================
//  Transform Functions
// ============================================================================

function transformAbpRoleToRole(
  abpRole: AbpIdentityRoleDto,
  userCount = 0,
  permissionCount = 0
): Role {
  return {
    id: abpRole.id,
    name: abpRole.name,
    description: undefined, // ABP doesn't have description field
    isDefault: abpRole.isDefault,
    isStatic: abpRole.isStatic,
    isPublic: abpRole.isPublic,
    userCount,
    permissionCount,
  }
}

// ============================================================================
//  Get All Roles (with user count and permission count)
// ============================================================================

export async function getRoles(): Promise<Role[]> {
  const result = await abpFetch<AbpPagedResult<AbpIdentityRoleDto>>(
    '/api/identity/roles?MaxResultCount=100'
  )

  if (result.items.length === 0) return []

  // Fetch permission counts for all roles in parallel
  const fetchCount = async (roleName: string): Promise<number> => {
    try {
      const permResult = await abpFetch<{
        groups: Array<{
          permissions: Array<{ name: string; isGranted: boolean; parentName?: string }>
        }>
      }>(`/api/permission-management/permissions?providerName=R&providerKey=${encodeURIComponent(roleName)}`)

      // Normalize: if any child is granted, ensure parent chain is also granted
      for (const group of permResult.groups) {
        const permsByName = new Map(group.permissions.map(p => [p.name, p]))
        let changed = true
        while (changed) {
          changed = false
          for (const perm of group.permissions) {
            if (perm.isGranted && perm.parentName) {
              const parent = permsByName.get(perm.parentName)
              if (parent && !parent.isGranted) {
                parent.isGranted = true
                changed = true
              }
            }
          }
        }
      }

      return permResult.groups.reduce((acc, group) =>
        acc + group.permissions.filter(p => p.isGranted).length, 0
      )
    } catch {
      return 0
    }
  }

  const rolesWithStats = await Promise.all(
    result.items.map(async (abpRole) => {
      const count = await fetchCount(abpRole.name)
      return transformAbpRoleToRole(abpRole, 0, count)
    })
  )

  return rolesWithStats
}

// ============================================================================
//  Get Single Role
// ============================================================================

export async function getRole(id: string): Promise<Role | null> {
  try {
    const abpRole = await abpFetch<AbpIdentityRoleDto>(`/api/identity/roles/${id}`)
    return transformAbpRoleToRole(abpRole)
  } catch (error) {
    console.error('[ABP Roles] Get role error:', error)
    return null
  }
}

// ============================================================================
//  Create Role
// ============================================================================

export async function createRole(input: CreateRoleInput): Promise<Role> {
  const createDto: AbpIdentityRoleCreateDto = {
    name: input.name,
    isDefault: input.isDefault,
    isPublic: input.isPublic,
  }

  const abpRole = await abpFetch<AbpIdentityRoleDto>('/api/identity/roles', {
    method: 'POST',
    body: JSON.stringify(createDto),
  })

  return transformAbpRoleToRole(abpRole)
}

// ============================================================================
//  Update Role
// ============================================================================

export async function updateRole(id: string, input: UpdateRoleInput): Promise<Role> {
  // Get current role to obtain concurrencyStamp
  const currentRole = await abpFetch<AbpIdentityRoleDto>(`/api/identity/roles/${id}`)

  if (currentRole.isStatic) {
    throw new Error('Cannot modify static role')
  }

  const updateDto: AbpIdentityRoleUpdateDto = {
    name: input.name,
    isDefault: input.isDefault,
    isPublic: input.isPublic,
    concurrencyStamp: currentRole.concurrencyStamp,
  }

  const abpRole = await abpFetch<AbpIdentityRoleDto>(`/api/identity/roles/${id}`, {
    method: 'PUT',
    body: JSON.stringify(updateDto),
  })

  return transformAbpRoleToRole(abpRole)
}

// ============================================================================
//  Delete Role
// ============================================================================

export async function deleteRole(id: string): Promise<void> {
  // Check if role is static before deleting
  const role = await abpFetch<AbpIdentityRoleDto>(`/api/identity/roles/${id}`)
  
  if (role.isStatic) {
    throw new Error('Cannot delete static role')
  }

  await abpFetch(`/api/identity/roles/${id}`, {
    method: 'DELETE',
  })
}

// ============================================================================
//  Get Role Permissions
// ============================================================================

export async function getRolePermissions(roleId: string): Promise<string[]> {
  try {
    // Get role name first
    const role = await abpFetch<AbpIdentityRoleDto>(`/api/identity/roles/${roleId}`)
    
    // Fetch permissions for role
    const result = await abpFetch<{
      groups: Array<{
        name: string
        displayName: string
        permissions: Array<{
          name: string
          displayName: string
          isGranted: boolean
        }>
      }>
    }>(`/api/permission-management/permissions?providerName=R&providerKey=${role.name}`)

    // Extract granted permission names
    const grantedPermissions: string[] = []
    for (const group of result.groups) {
      for (const permission of group.permissions) {
        if (permission.isGranted) {
          grantedPermissions.push(permission.name)
        }
      }
    }

    return grantedPermissions
  } catch (error) {
    console.error('[ABP Roles] Get role permissions error:', error)
    return []
  }
}

// ============================================================================
//  Update Role Permissions
// ============================================================================

export async function updateRolePermissions(
  roleId: string,
  permissions: string[]
): Promise<void> {
  // Get role name
  const role = await abpFetch<AbpIdentityRoleDto>(`/api/identity/roles/${roleId}`)

  // Get all permissions first
  const allPermissions = await abpFetch<{
    groups: Array<{
      permissions: Array<{
        name: string
        isGranted: boolean
      }>
    }>
  }>(`/api/permission-management/permissions?providerName=R&providerKey=${role.name}`)

  // Build permission update list
  const permissionUpdates: Array<{ name: string; isGranted: boolean }> = []
  
  for (const group of allPermissions.groups) {
    for (const permission of group.permissions) {
      permissionUpdates.push({
        name: permission.name,
        isGranted: permissions.includes(permission.name),
      })
    }
  }

  await abpFetch(`/api/permission-management/permissions?providerName=R&providerKey=${role.name}`, {
    method: 'PUT',
    body: JSON.stringify({ permissions: permissionUpdates }),
  })
}
