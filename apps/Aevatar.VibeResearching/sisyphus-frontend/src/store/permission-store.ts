import { create } from "zustand"
import { fetchMyPermissions } from "@/lib/abp/auth"

// ============================================================
//  Permission Store - User Permission State
// ============================================================

interface PermissionState {
  permissions: string[]
  isLoaded: boolean

  // Actions
  loadPermissions: () => Promise<void>
  clearPermissions: () => void
}

export const usePermissionStore = create<PermissionState>()((set) => ({
  permissions: [],
  isLoaded: false,

  loadPermissions: async () => {
    try {
      const permissions = await fetchMyPermissions()
      set({ permissions, isLoaded: true })
    } catch (error) {
      console.error("[PermissionStore] Failed to load permissions:", error)
      set({ permissions: [], isLoaded: true })
    }
  },

  clearPermissions: () => set({ permissions: [], isLoaded: false }),
}))

// Selectors
export const usePermissions = () => usePermissionStore((state) => state.permissions)
