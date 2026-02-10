import { create } from "zustand"
import { persist } from "zustand/middleware"
import type { AuthUser } from "@/types/user-management"

// ============================================================
//  Auth Store - User Authentication State
// ============================================================

interface AuthState {
  user: AuthUser | null
  isAuthenticated: boolean
  isLoading: boolean
  
  // Actions
  login: (user: AuthUser) => void
  logout: () => void
  updateUser: (updates: Partial<AuthUser>) => void
  setLoading: (loading: boolean) => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      isAuthenticated: false,
      isLoading: false,

      login: (user) => set({
        user,
        isAuthenticated: true,
        isLoading: false,
      }),

      logout: () => set({
        user: null,
        isAuthenticated: false,
        isLoading: false,
      }),

      updateUser: (updates) => set((state) => ({
        user: state.user ? { ...state.user, ...updates } : null,
      })),

      setLoading: (loading) => set({ isLoading: loading }),
    }),
    {
      name: "sisyphus-auth",
      partialize: (state) => ({
        user: state.user,
        isAuthenticated: state.isAuthenticated,
      }),
    }
  )
)

// Derived selectors
export const useIsAdmin = () => useAuthStore((state) => state.user?.isAdmin ?? false)
export const useCurrentUser = () => useAuthStore((state) => state.user)
export const useIsAuthenticated = () => useAuthStore((state) => state.isAuthenticated)
export const useHasPassword = () => useAuthStore((state) =>
  !state.user?.loginProvider || state.user.loginProvider === 'local'
)
