// ============================================================
//  Mock Permissions Service - Simulated Permission Management
// ============================================================

import type { PermissionGroup, PermissionProvider, PermissionProviderName } from "@/types/user-management"

const delay = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

// ============================================================
//  Permission Provider Configuration (ABP Standard)
// ============================================================

const permissionProviders: PermissionProvider[] = [
  {
    name: "R",
    displayName: "Role Permissions",
    description: "Manage permissions assigned to roles",
    icon: "shield",
    isEnabled: true,
  },
  {
    name: "U",
    displayName: "User Permissions",
    description: "Manage permissions assigned to specific users",
    icon: "user",
    isEnabled: true,
  },
  {
    name: "C",
    displayName: "Client Permissions",
    description: "Manage permissions for OAuth/OIDC clients",
    icon: "monitor",
    isEnabled: false, // Disabled by default - enable when OAuth is configured
  },
]

/**
 * Get available permission providers (ABP compliance)
 * In real ABP, this would come from backend configuration
 */
export async function getPermissionProviders(): Promise<PermissionProvider[]> {
  await delay(200)
  return permissionProviders.filter(p => p.isEnabled)
}

// Mock permission groups
const mockPermissionGroups: PermissionGroup[] = [
  {
    name: "AbpIdentity",
    displayName: "Identity Management",
    permissions: [
      {
        name: "AbpIdentity.Users",
        displayName: "User Management",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "AbpIdentity.Users.Create",
        displayName: "Create Users",
        parentName: "AbpIdentity.Users",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "AbpIdentity.Users.Update",
        displayName: "Update Users",
        parentName: "AbpIdentity.Users",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "AbpIdentity.Users.Delete",
        displayName: "Delete Users",
        parentName: "AbpIdentity.Users",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "AbpIdentity.Roles",
        displayName: "Role Management",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "AbpIdentity.Roles.Create",
        displayName: "Create Roles",
        parentName: "AbpIdentity.Roles",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "AbpIdentity.Roles.Update",
        displayName: "Update Roles",
        parentName: "AbpIdentity.Roles",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "AbpIdentity.Roles.Delete",
        displayName: "Delete Roles",
        parentName: "AbpIdentity.Roles",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
    ],
  },
  {
    name: "FeatureManagement",
    displayName: "Feature Management",
    permissions: [
      {
        name: "FeatureManagement.Features",
        displayName: "Manage Features",
        isGranted: false,
        allowedProviders: ["R"],
      },
    ],
  },
  {
    name: "Sisyphus",
    displayName: "Sisyphus Research",
    permissions: [
      {
        name: "Sisyphus.Sessions",
        displayName: "Session Management",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "Sisyphus.Sessions.Create",
        displayName: "Create Sessions",
        parentName: "Sisyphus.Sessions",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "Sisyphus.Sessions.Delete",
        displayName: "Delete Sessions",
        parentName: "Sisyphus.Sessions",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "Sisyphus.Agents",
        displayName: "Agent Management",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
      {
        name: "Sisyphus.Agents.Configure",
        displayName: "Configure Agents",
        parentName: "Sisyphus.Agents",
        isGranted: false,
        allowedProviders: ["R", "U"],
      },
    ],
  },
]

// Store granted permissions per provider
const grantedPermissions: Record<string, Set<string>> = {
  "R:admin": new Set([
    "AbpIdentity.Users",
    "AbpIdentity.Users.Create",
    "AbpIdentity.Users.Update",
    "AbpIdentity.Users.Delete",
    "AbpIdentity.Roles",
    "AbpIdentity.Roles.Create",
    "AbpIdentity.Roles.Update",
    "AbpIdentity.Roles.Delete",
    "FeatureManagement.Features",
    "Sisyphus.Sessions",
    "Sisyphus.Sessions.Create",
    "Sisyphus.Sessions.Delete",
    "Sisyphus.Agents",
    "Sisyphus.Agents.Configure",
  ]),
  "R:member": new Set([
    "Sisyphus.Sessions",
    "Sisyphus.Sessions.Create",
  ]),
  "R:viewer": new Set([
    "AbpIdentity.Users",
    "AbpIdentity.Roles",
    "FeatureManagement.Features",
    "Sisyphus.Sessions",
    "Sisyphus.Agents",
    "Sisyphus.Agents.Configure",
  ]),
}

/**
 * Get permission groups with grant status for a provider
 */
export async function getPermissions(
  providerName: PermissionProviderName,
  providerKey: string
): Promise<PermissionGroup[]> {
  await delay(400)

  const key = `${providerName}:${providerKey}`
  const granted = grantedPermissions[key] || new Set()

  return mockPermissionGroups.map(group => ({
    ...group,
    permissions: group.permissions.map(perm => ({
      ...perm,
      isGranted: granted.has(perm.name),
    })),
  }))
}

/**
 * Update permissions for a provider
 */
export async function updatePermissions(
  providerName: PermissionProviderName,
  providerKey: string,
  permissions: { name: string; isGranted: boolean }[]
): Promise<void> {
  await delay(500)

  const key = `${providerName}:${providerKey}`
  if (!grantedPermissions[key]) {
    grantedPermissions[key] = new Set()
  }

  permissions.forEach(p => {
    if (p.isGranted) {
      grantedPermissions[key].add(p.name)
    } else {
      grantedPermissions[key].delete(p.name)
    }
  })
}

/**
 * Grant all permissions for a provider (filtered by allowedProviders)
 */
export async function grantAllPermissions(
  providerName: PermissionProviderName,
  providerKey: string
): Promise<void> {
  await delay(400)

  const key = `${providerName}:${providerKey}`
  // Only grant permissions allowed for this provider type
  grantedPermissions[key] = new Set(
    mockPermissionGroups.flatMap(g => 
      g.permissions
        .filter(p => p.allowedProviders.includes(providerName))
        .map(p => p.name)
    )
  )
}

/**
 * Revoke all permissions for a provider
 */
export async function revokeAllPermissions(
  providerName: PermissionProviderName,
  providerKey: string
): Promise<void> {
  await delay(400)

  const key = `${providerName}:${providerKey}`
  grantedPermissions[key] = new Set()
}

/**
 * Get all permission groups (without grant status)
 */
export async function getAllPermissionGroups(): Promise<PermissionGroup[]> {
  await delay(300)
  return mockPermissionGroups
}
