import React, { useEffect, useMemo } from "react"
import { NavLink, useLocation } from "react-router-dom"
import { User, Lock, Users, Shield, Key, Settings } from "lucide-react"
import { cn } from "@/lib/utils"
import { useAuthStore } from "@/store/auth-store"
import { usePermissionStore } from "@/store/permission-store"

// ============================================================
//  Account Sidebar - Navigation Menu
// ============================================================

interface NavItem {
  label: string
  href: string
  icon: React.ReactNode
  /** ABP permission(s) required — if array, ANY match grants access */
  permission?: string | string[]
}

const settingsItems: NavItem[] = [
  { label: "Profile", href: "/account/profile", icon: <User className="w-4 h-4" /> },
  { label: "Password", href: "/account/password", icon: <Lock className="w-4 h-4" /> },
]

const platformItem: NavItem = { 
  label: "Platform Settings", 
  href: "/admin/settings", 
  icon: <Settings className="w-4 h-4" /> 
}

const allManagementItems: NavItem[] = [
  { label: "Users", href: "/admin/users", icon: <Users className="w-4 h-4" />, permission: "AbpIdentity.Users" },
  { label: "Roles", href: "/admin/roles", icon: <Shield className="w-4 h-4" />, permission: ["AbpIdentity.Roles.Create", "AbpIdentity.Roles.Update", "AbpIdentity.Roles.Delete", "AbpIdentity.Roles.ManagePermissions"] },
  { label: "Permissions", href: "/admin/permissions", icon: <Key className="w-4 h-4" />, permission: ["AbpIdentity.Roles.ManagePermissions", "AbpIdentity.Users.ManagePermissions"] },
]

interface AccountSidebarProps {
  isAdmin: boolean
}

export const AccountSidebar: React.FC<AccountSidebarProps> = ({ isAdmin }) => {
  const location = useLocation()
  const { isAuthenticated } = useAuthStore()
  const permissions = usePermissionStore((s) => s.permissions)
  const isPermLoaded = usePermissionStore((s) => s.isLoaded)
  const loadPermissions = usePermissionStore((s) => s.loadPermissions)

  // Auto-load permissions if not yet loaded
  useEffect(() => {
    if (isAuthenticated && !isPermLoaded) {
      loadPermissions()
    }
  }, [isAuthenticated, isPermLoaded, loadPermissions])

  // Filter management items based on permissions
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

  const renderNavItem = (item: NavItem, isAdminSection = false) => {
    const isActive = location.pathname === item.href

    // Settings items use cyan, Admin items use gold when active
    const activeClasses = isAdminSection
      ? "bg-neon-gold/15 text-neon-gold border border-neon-gold/40"
      : "bg-neon-cyan/15 text-neon-cyan border border-neon-cyan/40"

    const activeIconColor = isAdminSection ? "text-neon-gold" : "text-neon-cyan"

    return (
      <NavLink
        key={item.href}
        to={item.href}
        className={cn(
          "flex items-center gap-2.5 px-3 py-2.5 rounded-lg text-[13px] transition-all",
          isActive
            ? cn(activeClasses, "font-medium")
            : "text-text-muted hover:text-text-secondary hover:bg-surface-elevated border border-transparent"
        )}
      >
        <span className={cn(isActive && activeIconColor)}>{item.icon}</span>
        <span>{item.label}</span>
      </NavLink>
    )
  }

  return (
    <aside className="w-60 min-h-[calc(100vh-4rem)] bg-surface border-r border-border-subtle p-4">
      {/* Settings Section */}
      <p className="text-[10px] font-mono font-semibold uppercase tracking-wider text-text-dimmed mb-3 px-3">
        Settings
      </p>
      <nav className="space-y-1">
        {settingsItems.map((item) => renderNavItem(item, false))}
      </nav>

      {/* Management Section — show based on permissions */}
      {managementItems.length > 0 && (
        <div className="mt-4 pt-4 border-t border-border-subtle">
          <p className="text-[10px] font-mono font-semibold uppercase tracking-wider text-neon-gold mb-3 px-3">
            Management
          </p>
          <nav className="space-y-1">
            {managementItems.map((item) => renderNavItem(item, true))}
          </nav>
        </div>
      )}

      {/* Platform Settings - Always at bottom */}
      <div className="mt-4 pt-4 border-t border-border-subtle">
        <nav className="space-y-1">
          {renderNavItem(platformItem, false)}
        </nav>
      </div>
    </aside>
  )
}

export default AccountSidebar
