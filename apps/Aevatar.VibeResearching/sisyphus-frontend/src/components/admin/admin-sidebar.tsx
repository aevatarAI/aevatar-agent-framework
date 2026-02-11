import React, { useEffect, useMemo } from "react"
import { NavLink, useLocation } from "react-router-dom"
import { User, Lock, Users, Shield, Key, Settings } from "lucide-react"
import { cn } from "@/lib/utils"
import { useAuthStore, useIsAdmin } from "@/store/auth-store"
import { usePermissionStore } from "@/store/permission-store"

// ============================================================
//  Admin Sidebar - Navigation with Settings + Management
// ============================================================

interface NavItem {
  label: string
  href: string
  icon: React.ReactNode
  /** ABP permission(s) required — if array, ANY match grants access (admin always sees all) */
  permission?: string | string[]
}

const settingsItems: NavItem[] = [
  { label: "Profile", href: "/account/profile", icon: <User className="w-4 h-4" /> },
  { label: "Password", href: "/account/password", icon: <Lock className="w-4 h-4" /> },
]

const allManagementItems: NavItem[] = [
  { label: "Users", href: "/admin/users", icon: <Users className="w-4 h-4" />, permission: "AbpIdentity.Users" },
  { label: "Roles", href: "/admin/roles", icon: <Shield className="w-4 h-4" />, permission: ["AbpIdentity.Roles.Create", "AbpIdentity.Roles.Update", "AbpIdentity.Roles.Delete", "AbpIdentity.Roles.ManagePermissions"] },
  { label: "Permissions", href: "/admin/permissions", icon: <Key className="w-4 h-4" />, permission: ["AbpIdentity.Roles.ManagePermissions", "AbpIdentity.Users.ManagePermissions"] },
]

const platformItem: NavItem = { 
  label: "Platform Settings", 
  href: "/admin/settings", 
  icon: <Settings className="w-4 h-4" /> 
}

export const AdminSidebar: React.FC = () => {
  const location = useLocation()
  const isAdmin = useIsAdmin()
  const { isAuthenticated } = useAuthStore()
  const permissions = usePermissionStore((s) => s.permissions)
  const isPermLoaded = usePermissionStore((s) => s.isLoaded)
  const loadPermissions = usePermissionStore((s) => s.loadPermissions)

  // Auto-load permissions if not yet loaded (e.g. navigating from account pages)
  useEffect(() => {
    if (isAuthenticated && !isPermLoaded) {
      loadPermissions()
    }
  }, [isAuthenticated, isPermLoaded, loadPermissions])

  // Filter management items based on user permissions
  const managementItems = useMemo(() => {
    if (isAdmin) return allManagementItems
    return allManagementItems.filter(item => {
      if (!item.permission) return true
      if (Array.isArray(item.permission)) {
        return item.permission.some(p => permissions.includes(p))
      }
      return permissions.includes(item.permission)
    })
  }, [isAdmin, permissions])

  const renderNavItem = (item: NavItem, useGold = false) => {
    const isActive = location.pathname === item.href
    const activeColor = useGold ? "neon-gold" : "neon-cyan"

    return (
      <NavLink
        key={item.href}
        to={item.href}
        className={cn(
          "flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-all",
          isActive
            ? `bg-${activeColor}/15 text-${activeColor} border border-${activeColor}/40`
            : "text-text-muted hover:text-text-primary hover:bg-surface-elevated border border-transparent"
        )}
        style={isActive ? {
          backgroundColor: useGold ? "rgba(252, 211, 77, 0.15)" : "rgba(125, 211, 252, 0.15)",
          color: useGold ? "#fcd34d" : "#7dd3fc",
          borderColor: useGold ? "rgba(252, 211, 77, 0.4)" : "rgba(125, 211, 252, 0.4)",
        } : undefined}
      >
        <span style={isActive ? { color: useGold ? "#fcd34d" : "#7dd3fc" } : undefined}>
          {item.icon}
        </span>
        <span>{item.label}</span>
      </NavLink>
    )
  }

  return (
    <aside className="w-56 min-h-[calc(100vh-4rem)] bg-surface border-r border-border-subtle p-4">
      {/* Settings Section */}
      <p className="text-[10px] font-mono font-semibold uppercase tracking-wider text-text-dimmed mb-3 px-3">
        Settings
      </p>
      <nav className="space-y-1">
        {settingsItems.map((item) => renderNavItem(item, false))}
      </nav>

      {/* Management Section — only show if user has any management permissions */}
      {managementItems.length > 0 && (
        <>
          <div className="my-4 h-px bg-border-subtle" />
          <p className="text-[10px] font-mono font-semibold uppercase tracking-wider text-text-dimmed mb-3 px-3">
            Management
          </p>
          <nav className="space-y-1">
            {managementItems.map((item) => renderNavItem(item, true))}
          </nav>
        </>
      )}

      {/* Divider */}
      <div className="my-4 h-px bg-border-subtle" />

      {/* Platform Settings - Always at bottom */}
      <nav className="space-y-1">
        {renderNavItem(platformItem, false)}
      </nav>
    </aside>
  )
}

export default AdminSidebar
