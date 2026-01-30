import { useCallback, useMemo } from "react"
import { useAuthStore } from "@/store/auth-store"
import { usePermissionStore } from "@/store/permission-store"

// ============================================================
//  usePermission - Centralized permission check hook
// ============================================================

export function usePermission() {
  const permissions = usePermissionStore((s) => s.permissions)
  const isLoaded = usePermissionStore((s) => s.isLoaded)
  const user = useAuthStore((s) => s.user)
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  const isAdmin = user?.isAdmin ?? false

  const hasPermission = useCallback(
    (name: string) => permissions.includes(name),
    [permissions]
  )

  const hasAnyPermission = useCallback(
    (names: string[]) => names.some((n) => permissions.includes(n)),
    [permissions]
  )

  const isOwner = useCallback(
    (resourceOwnerId?: string) => {
      if (!isAuthenticated || !user || !resourceOwnerId) return false
      return user.id === resourceOwnerId
    },
    [isAuthenticated, user]
  )

  const canManageSession = useCallback(
    (session: { ownerId?: string }) => {
      if (isAdmin) return true
      if (!isAuthenticated) return false
      // Legacy sessions without ownerId are accessible to any authenticated user
      if (!session.ownerId) return true
      return isOwner(session.ownerId)
    },
    [isAdmin, isAuthenticated, isOwner]
  )

  return useMemo(
    () => ({
      permissions,
      isLoading: !isLoaded,
      hasPermission,
      hasAnyPermission,
      isAdmin,
      isMember: isAuthenticated && !isAdmin,
      isAnonymous: !isAuthenticated,
      isAuthenticated,
      isOwner,
      canManageSession,
    }),
    [permissions, isLoaded, hasPermission, hasAnyPermission, isAdmin, isAuthenticated, isOwner, canManageSession]
  )
}
