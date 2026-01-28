// ============================================================
//  Mock Roles Service - Simulated Role Management
// ============================================================

import type { Role, CreateRoleInput, UpdateRoleInput } from "@/types/user-management"

const delay = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

// Mock role database
let mockRoles: Role[] = [
  {
    id: "1",
    name: "admin",
    description: "Full system access with all permissions",
    isDefault: false,
    isStatic: true,
    isPublic: true,
    userCount: 1,
    permissionCount: 45,
  },
  {
    id: "2",
    name: "member",
    description: "Standard user with basic access",
    isDefault: true,
    isStatic: false,
    isPublic: true,
    userCount: 2,
    permissionCount: 12,
  },
  {
    id: "3",
    name: "viewer",
    description: "Read-only access to resources",
    isDefault: false,
    isStatic: false,
    isPublic: true,
    userCount: 1,
    permissionCount: 6,
  },
]

/**
 * Get all roles
 */
export async function getRoles(): Promise<Role[]> {
  await delay(400)
  return [...mockRoles]
}

/**
 * Get single role by ID
 */
export async function getRole(id: string): Promise<Role | null> {
  await delay(300)
  return mockRoles.find(r => r.id === id) || null
}

/**
 * Create new role
 */
export async function createRole(input: CreateRoleInput): Promise<Role> {
  await delay(500)

  const newRole: Role = {
    id: String(Date.now()),
    name: input.name,
    isDefault: input.isDefault,
    isStatic: false,
    isPublic: input.isPublic,
    userCount: 0,
    permissionCount: 0,
  }

  mockRoles.push(newRole)
  return newRole
}

/**
 * Update existing role
 */
export async function updateRole(id: string, input: UpdateRoleInput): Promise<Role> {
  await delay(400)

  const index = mockRoles.findIndex(r => r.id === id)
  if (index === -1) {
    throw new Error("Role not found")
  }

  // Can't modify static roles
  if (mockRoles[index].isStatic) {
    throw new Error("Cannot modify static role")
  }

  mockRoles[index] = {
    ...mockRoles[index],
    name: input.name,
    isDefault: input.isDefault,
    isPublic: input.isPublic,
  }

  return mockRoles[index]
}

/**
 * Delete role
 */
export async function deleteRole(id: string): Promise<void> {
  await delay(400)

  const role = mockRoles.find(r => r.id === id)
  if (role?.isStatic) {
    throw new Error("Cannot delete static role")
  }

  mockRoles = mockRoles.filter(r => r.id !== id)
}

/**
 * Get role permissions
 */
export async function getRolePermissions(roleId: string): Promise<string[]> {
  await delay(300)

  // Mock permissions by role
  const permissionsByRole: Record<string, string[]> = {
    "1": [
      "AbpIdentity.Users",
      "AbpIdentity.Users.Create",
      "AbpIdentity.Users.Update",
      "AbpIdentity.Users.Delete",
      "AbpIdentity.Roles",
      "AbpIdentity.Roles.Create",
      "AbpIdentity.Roles.Update",
      "AbpIdentity.Roles.Delete",
    ],
    "2": [
      "AbpIdentity.Users",
    ],
    "3": [],
  }

  return permissionsByRole[roleId] || []
}

/**
 * Update role permissions
 */
export async function updateRolePermissions(_roleId: string, _permissions: string[]): Promise<void> {
  await delay(500)
  // In real app, this would update the permissions
}
