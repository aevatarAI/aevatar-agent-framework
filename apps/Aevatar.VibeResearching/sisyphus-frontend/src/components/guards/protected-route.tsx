import React, { useEffect } from "react"
import { Navigate, useLocation } from "react-router-dom"
import { useAuthStore } from "@/store/auth-store"
import { usePermissionStore } from "@/store/permission-store"

// ============================================================
//  Loading Spinner (shared)
// ============================================================

const LoadingSpinner: React.FC = () => (
  <div className="min-h-screen bg-background flex items-center justify-center">
    <div className="flex items-center gap-3">
      <div className="w-8 h-8 border-2 border-neon-cyan border-t-transparent rounded-full animate-spin" />
      <span className="text-text-muted">Loading...</span>
    </div>
  </div>
)

// ============================================================
//  Protected Route - Requires Authentication
// ============================================================

interface ProtectedRouteProps {
  children: React.ReactNode
}

export const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ children }) => {
  const { isAuthenticated, isLoading } = useAuthStore()
  const location = useLocation()

  if (isLoading) return <LoadingSpinner />

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  return <>{children}</>
}

// ============================================================
//  Admin Route - Requires Admin Role
// ============================================================

interface AdminRouteProps {
  children: React.ReactNode
}

export const AdminRoute: React.FC<AdminRouteProps> = ({ children }) => {
  const { isAuthenticated, isLoading, user } = useAuthStore()
  const location = useLocation()

  if (isLoading) return <LoadingSpinner />

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  if (!user?.isAdmin) {
    return <Navigate to="/403" replace />
  }

  return <>{children}</>
}

// ============================================================
//  Permission Route - Requires specific permission(s)
//  User passes if they have ANY of the listed permissions,
//  OR if they are admin (admin bypasses all checks).
// ============================================================

interface PermissionRouteProps {
  children: React.ReactNode
  /** User must have at least one of these permissions */
  requiredPermissions: string[]
}

export const PermissionRoute: React.FC<PermissionRouteProps> = ({ children, requiredPermissions }) => {
  const { isAuthenticated, isLoading, user } = useAuthStore()
  const permissions = usePermissionStore((s) => s.permissions)
  const isPermLoaded = usePermissionStore((s) => s.isLoaded)
  const loadPermissions = usePermissionStore((s) => s.loadPermissions)
  const location = useLocation()

  // Auto-load permissions if authenticated but not yet loaded (e.g. after page refresh)
  useEffect(() => {
    if (isAuthenticated && !isPermLoaded) {
      loadPermissions()
    }
  }, [isAuthenticated, isPermLoaded, loadPermissions])

  if (isLoading) return <LoadingSpinner />

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  // Admin bypasses permission checks
  if (user?.isAdmin) {
    return <>{children}</>
  }

  // Wait for permissions to load before deciding
  if (!isPermLoaded) return <LoadingSpinner />

  // Check if user has any of the required permissions
  const hasRequired = requiredPermissions.some(p => permissions.includes(p))
  if (!hasRequired) {
    return <Navigate to="/403" replace />
  }

  return <>{children}</>
}
