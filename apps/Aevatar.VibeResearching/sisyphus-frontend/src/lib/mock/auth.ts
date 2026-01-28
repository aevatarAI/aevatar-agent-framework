// ============================================================
//  Mock Auth Service - Simulated Authentication
// ============================================================

import type { AuthUser, LoginInput, RegisterInput } from "@/types/user-management"

// Simulated delay for API calls
const delay = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

// Mock user database
const mockUsers: AuthUser[] = [
  {
    id: "1",
    userName: "admin",
    email: "admin@sisyphus.ai",
    name: "John",
    surname: "Doe",
    roles: ["admin", "member"],
    isAdmin: true,
    avatarUrl: undefined,
  },
  {
    id: "2",
    userName: "user",
    email: "user@sisyphus.ai",
    name: "Jane",
    surname: "Smith",
    roles: ["member"],
    isAdmin: false,
    avatarUrl: undefined,
  },
]

// Mock passwords (in real app, never store plain text)
// Passwords must match ABP policy: uppercase + lowercase + number + special char
const mockPasswords: Record<string, string> = {
  "admin@sisyphus.ai": "Admin@123",
  "user@sisyphus.ai": "User@123",
}

export interface AuthResponse {
  success: boolean
  user?: AuthUser
  error?: string
}

/**
 * Mock login - validates email/password
 */
export async function mockLogin(input: LoginInput): Promise<AuthResponse> {
  await delay(800) // Simulate network delay

  const { email, password } = input
  
  // Check if user exists
  const user = mockUsers.find(u => u.email.toLowerCase() === email.toLowerCase())
  if (!user) {
    return {
      success: false,
      error: "The email or password you entered is incorrect.",
    }
  }

  // Check password
  if (mockPasswords[user.email] !== password) {
    return {
      success: false,
      error: "The email or password you entered is incorrect.",
    }
  }

  return {
    success: true,
    user,
  }
}

/**
 * Mock register - creates new user
 */
export async function mockRegister(input: RegisterInput): Promise<AuthResponse> {
  await delay(1000)

  const { email, userName, password, name, surname } = input

  // Check if email already exists
  if (mockUsers.some(u => u.email.toLowerCase() === email.toLowerCase())) {
    return {
      success: false,
      error: "An account with this email already exists.",
    }
  }

  // Check if username already exists
  if (mockUsers.some(u => u.userName.toLowerCase() === userName.toLowerCase())) {
    return {
      success: false,
      error: "This username is already taken.",
    }
  }

  // Create new user
  const newUser: AuthUser = {
    id: String(mockUsers.length + 1),
    userName,
    email,
    name: name || undefined,
    surname: surname || undefined,
    roles: ["member"],
    isAdmin: false,
    avatarUrl: undefined,
  }

  mockUsers.push(newUser)
  mockPasswords[email] = password

  return {
    success: true,
    user: newUser,
  }
}

/**
 * Mock forgot password - sends reset email
 */
export async function mockForgotPassword(_email: string): Promise<{ success: boolean; error?: string }> {
  await delay(800)
  
  // Always return success to prevent email enumeration
  return { success: true }
}

/**
 * Mock reset password - resets password with token
 */
export async function mockResetPassword(
  _token: string,
  _newPassword: string
): Promise<{ success: boolean; error?: string }> {
  await delay(800)

  // In real app, validate token
  // For mock, just return success
  return { success: true }
}

/**
 * Mock verify email
 */
export async function mockVerifyEmail(_token: string): Promise<{ success: boolean; error?: string }> {
  await delay(500)
  return { success: true }
}

/**
 * Mock resend verification email
 */
export async function mockResendVerification(_email: string): Promise<{ success: boolean; error?: string }> {
  await delay(500)
  return { success: true }
}

/**
 * Mock social login
 */
export async function mockSocialLogin(provider: "google" | "github"): Promise<AuthResponse> {
  await delay(1000)

  // Simulate successful social login with a new/existing user
  const socialUser: AuthUser = {
    id: "social-1",
    userName: `${provider}_user`,
    email: `${provider}.user@example.com`,
    name: "Social",
    surname: "User",
    roles: ["member"],
    isAdmin: false,
    avatarUrl: undefined,
  }

  return {
    success: true,
    user: socialUser,
  }
}

/**
 * Get mock user by ID
 */
export function getMockUser(id: string): AuthUser | undefined {
  return mockUsers.find(u => u.id === id)
}

/**
 * Get demo credentials for testing
 * Passwords follow ABP policy: uppercase + lowercase + number + special char
 */
export function getDemoCredentials() {
  return {
    admin: { email: "admin@sisyphus.ai", password: "Admin@123" },
    user: { email: "user@sisyphus.ai", password: "User@123" },
  }
}
