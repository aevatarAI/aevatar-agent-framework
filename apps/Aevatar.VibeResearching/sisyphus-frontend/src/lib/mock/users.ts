// ============================================================
//  Mock Users Service - Simulated User Management
// ============================================================

import type { User, CreateUserInput, UpdateUserInput, PagedResult } from "@/types/user-management"

const delay = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

// Mock user database
let mockUsers: User[] = [
  {
    id: "1",
    userName: "admin",
    email: "admin@sisyphus.ai",
    name: "John",
    surname: "Doe",
    phoneNumber: "+1 (555) 123-4567",
    isActive: true,
    lockoutEnabled: false,
    emailConfirmed: true,
    twoFactorEnabled: true,
    createdAt: "2024-01-15T10:30:00Z",
    lastLoginTime: "2024-01-20T08:45:00Z",
    roles: ["admin", "member"],
    avatarUrl: undefined,
  },
  {
    id: "2",
    userName: "alice.smith",
    email: "alice.smith@example.com",
    name: "Alice",
    surname: "Smith",
    phoneNumber: "+1 (555) 234-5678",
    isActive: true,
    lockoutEnabled: false,
    emailConfirmed: true,
    twoFactorEnabled: false,
    createdAt: "2024-01-16T14:20:00Z",
    lastLoginTime: "2024-01-19T16:30:00Z",
    roles: ["member"],
    avatarUrl: undefined,
  },
  {
    id: "3",
    userName: "bob.wilson",
    email: "bob.wilson@example.com",
    name: "Bob",
    surname: "Wilson",
    phoneNumber: undefined,
    isActive: false,
    lockoutEnabled: true,
    emailConfirmed: false,
    twoFactorEnabled: false,
    createdAt: "2024-01-17T09:15:00Z",
    lastLoginTime: undefined,
    roles: ["viewer"],
    avatarUrl: undefined,
  },
]

/**
 * Get paginated list of users
 */
export async function getUsers(params?: {
  skip?: number
  take?: number
  search?: string
  role?: string
  status?: "active" | "inactive"
}): Promise<PagedResult<User>> {
  await delay(500)

  let filtered = [...mockUsers]

  // Apply search filter
  if (params?.search) {
    const search = params.search.toLowerCase()
    filtered = filtered.filter(u =>
      u.userName.toLowerCase().includes(search) ||
      u.email.toLowerCase().includes(search) ||
      `${u.name} ${u.surname}`.toLowerCase().includes(search)
    )
  }

  // Apply role filter
  if (params?.role) {
    filtered = filtered.filter(u => u.roles.includes(params.role!))
  }

  // Apply status filter
  if (params?.status) {
    filtered = filtered.filter(u =>
      params.status === "active" ? u.isActive : !u.isActive
    )
  }

  const totalCount = filtered.length
  const skip = params?.skip || 0
  const take = params?.take || 10
  const items = filtered.slice(skip, skip + take)

  return { items, totalCount }
}

/**
 * Get single user by ID
 */
export async function getUser(id: string): Promise<User | null> {
  await delay(300)
  return mockUsers.find(u => u.id === id) || null
}

/**
 * Create new user
 */
export async function createUser(input: CreateUserInput): Promise<User> {
  await delay(600)

  const newUser: User = {
    id: String(Date.now()),
    userName: input.userName,
    email: input.email,
    name: input.name || undefined,
    surname: input.surname || undefined,
    phoneNumber: input.phoneNumber || undefined,
    isActive: input.isActive,
    lockoutEnabled: input.lockoutEnabled,
    emailConfirmed: false,
    twoFactorEnabled: false,
    createdAt: new Date().toISOString(),
    lastLoginTime: undefined,
    roles: input.roleNames,
    avatarUrl: undefined,
  }

  mockUsers.push(newUser)
  return newUser
}

/**
 * Update existing user
 */
export async function updateUser(id: string, input: UpdateUserInput): Promise<User> {
  await delay(500)

  const index = mockUsers.findIndex(u => u.id === id)
  if (index === -1) {
    throw new Error("User not found")
  }

  mockUsers[index] = {
    ...mockUsers[index],
    userName: input.userName,
    email: input.email,
    name: input.name || undefined,
    surname: input.surname || undefined,
    phoneNumber: input.phoneNumber || undefined,
    isActive: input.isActive,
    lockoutEnabled: input.lockoutEnabled,
    roles: input.roleNames,
  }

  return mockUsers[index]
}

/**
 * Delete user
 */
export async function deleteUser(id: string): Promise<void> {
  await delay(400)
  mockUsers = mockUsers.filter(u => u.id !== id)
}

/**
 * Set user password
 */
export async function setUserPassword(_id: string, _password: string): Promise<void> {
  await delay(400)
  // In real app, this would update the password
}

/**
 * Lock/unlock user account
 */
export async function toggleUserLock(id: string, locked: boolean): Promise<User> {
  await delay(300)

  const index = mockUsers.findIndex(u => u.id === id)
  if (index === -1) {
    throw new Error("User not found")
  }

  mockUsers[index].lockoutEnabled = locked
  return mockUsers[index]
}

/**
 * Get user statistics
 */
export async function getUserStats(): Promise<{
  total: number
  active: number
  roles: number
  inactive: number
}> {
  await delay(200)
  
  return {
    total: mockUsers.length,
    active: mockUsers.filter(u => u.isActive).length,
    roles: new Set(mockUsers.flatMap(u => u.roles)).size,
    inactive: mockUsers.filter(u => !u.isActive).length,
  }
}
