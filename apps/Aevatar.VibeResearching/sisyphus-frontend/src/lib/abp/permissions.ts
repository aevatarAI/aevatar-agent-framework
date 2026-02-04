// ============================================================================
//  ABP Permissions API - Permission Management
//  Includes both admin permission management and current user permissions
// ============================================================================

import type {
  PermissionGroup,
  PermissionProvider,
  PermissionProviderName,
} from '@/types/user-management'
import { abpFetch } from './config'

// ============================================================================
//  Get My Permissions (Current User) - New Backend API
// ============================================================================

export async function getMyPermissions(): Promise<string[]> {
  try {
    const result = await abpFetch<{ permissions: string[] }>('/api/account/my-permissions', {
      credentials: 'include',
    } as RequestInit)
    return result.permissions
  } catch (error) {
    console.error('[Permissions] Get my permissions error:', error)
    return []
  }
}

// ============================================================================
//  Check Single Permission
// ============================================================================

export async function hasPermission(permissionName: string): Promise<boolean> {
  const permissions = await getMyPermissions()
  return permissions.includes(permissionName)
}

// ============================================================================
//  Check Multiple Permissions (any)
// ============================================================================

export async function hasAnyPermission(permissionNames: string[]): Promise<boolean> {
  const permissions = await getMyPermissions()
  return permissionNames.some((p) => permissions.includes(p))
}

// ============================================================================
//  Check Multiple Permissions (all)
// ============================================================================

export async function hasAllPermissions(permissionNames: string[]): Promise<boolean> {
  const permissions = await getMyPermissions()
  return permissionNames.every((p) => permissions.includes(p))
}

// === ABP Permission Response ===

interface AbpPermissionGroupDto {
  name: string
  displayName: string
  permissions: AbpPermissionDto[]
}

interface AbpPermissionDto {
  name: string
  displayName: string
  parentName?: string
  isGranted: boolean
  grantedProviders: Array<{
    providerName: string
    providerKey: string
  }>
  allowedProviders: string[]
}

interface AbpPermissionsResponse {
  entityDisplayName: string
  groups: AbpPermissionGroupDto[]
}

// ============================================================================
//  Permission Providers (ABP Standard)
// ============================================================================

const permissionProviders: PermissionProvider[] = [
  {
    name: 'R',
    displayName: 'Role Permissions',
    description: 'Manage permissions assigned to roles',
    icon: 'shield',
    isEnabled: true,
  },
  {
    name: 'U',
    displayName: 'User Permissions',
    description: 'Manage permissions assigned to specific users',
    icon: 'user',
    isEnabled: true,
  },
  {
    name: 'C',
    displayName: 'Client Permissions',
    description: 'Manage permissions for OAuth/OIDC clients',
    icon: 'monitor',
    isEnabled: false,
  },
]

// ============================================================================
//  Get Permission Providers
// ============================================================================

export async function getPermissionProviders(): Promise<PermissionProvider[]> {
  return permissionProviders.filter((p) => p.isEnabled)
}

// ============================================================================
//  Get Permissions for Provider
// ============================================================================

export async function getPermissions(
  providerName: PermissionProviderName,
  providerKey: string
): Promise<PermissionGroup[]> {
  try {
    const result = await abpFetch<AbpPermissionsResponse>(
      `/api/permission-management/permissions?providerName=${providerName}&providerKey=${encodeURIComponent(providerKey)}`
    )

    return result.groups.map((group) => ({
      name: group.name,
      displayName: group.displayName,
      permissions: group.permissions.map((perm) => ({
        name: perm.name,
        displayName: perm.displayName,
        parentName: perm.parentName,
        isGranted: perm.isGranted,
        allowedProviders: perm.allowedProviders as PermissionProviderName[],
        // Include grant source for showing inherited permissions
        grantedProviders: perm.grantedProviders.map(gp => ({
          providerName: gp.providerName as PermissionProviderName,
          providerKey: gp.providerKey,
        })),
      })),
    }))
  } catch (error) {
    console.error('[ABP Permissions] Get permissions error:', error)
    return []
  }
}

// ============================================================================
//  Update Permissions
// ============================================================================

export async function updatePermissions(
  providerName: PermissionProviderName,
  providerKey: string,
  permissions: { name: string; isGranted: boolean }[]
): Promise<void> {
  await abpFetch(
    `/api/permission-management/permissions?providerName=${providerName}&providerKey=${encodeURIComponent(providerKey)}`,
    {
      method: 'PUT',
      body: JSON.stringify({ permissions }),
    }
  )
}

// ============================================================================
//  Grant All Permissions
// ============================================================================

export async function grantAllPermissions(
  providerName: PermissionProviderName,
  providerKey: string
): Promise<void> {
  // Get all permissions first
  const groups = await getPermissions(providerName, providerKey)

  // Build permission list with all granted
  // NOTE: Empty allowedProviders array means permission is allowed for ALL provider types
  const permissions: { name: string; isGranted: boolean }[] = []
  
  for (const group of groups) {
    for (const perm of group.permissions) {
      // Grant if allowedProviders is empty (all allowed) or includes this provider
      const isAllowed = perm.allowedProviders.length === 0 || 
                       perm.allowedProviders.includes(providerName)
      if (isAllowed) {
        permissions.push({ name: perm.name, isGranted: true })
      }
    }
  }

  await updatePermissions(providerName, providerKey, permissions)
}

// ============================================================================
//  Revoke All Permissions
// ============================================================================

export async function revokeAllPermissions(
  providerName: PermissionProviderName,
  providerKey: string
): Promise<void> {
  // Get all permissions first
  const groups = await getPermissions(providerName, providerKey)

  // Build permission list with all revoked
  const permissions: { name: string; isGranted: boolean }[] = []
  
  for (const group of groups) {
    for (const perm of group.permissions) {
      permissions.push({ name: perm.name, isGranted: false })
    }
  }

  await updatePermissions(providerName, providerKey, permissions)
}

// ============================================================================
//  Get All Permission Groups (without grant status)
// ============================================================================

export async function getAllPermissionGroups(): Promise<PermissionGroup[]> {
  // Use a dummy provider to get all available permissions
  // The structure is the same, just ignore isGranted
  return getPermissions('R', 'admin')
}
