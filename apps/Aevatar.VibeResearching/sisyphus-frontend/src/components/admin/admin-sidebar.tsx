import React from "react"
import { NavLink, useLocation } from "react-router-dom"
import { User, Lock, Users, Shield, Key, Settings } from "lucide-react"
import { cn } from "@/lib/utils"

// ============================================================
//  Admin Sidebar - Navigation with Settings + Management
// ============================================================

interface NavItem {
  label: string
  href: string
  icon: React.ReactNode
}

const settingsItems: NavItem[] = [
  { label: "Profile", href: "/account/profile", icon: <User className="w-4 h-4" /> },
  { label: "Password", href: "/account/password", icon: <Lock className="w-4 h-4" /> },
]

const managementItems: NavItem[] = [
  { label: "Users", href: "/admin/users", icon: <Users className="w-4 h-4" /> },
  { label: "Roles", href: "/admin/roles", icon: <Shield className="w-4 h-4" /> },
  { label: "Permissions", href: "/admin/permissions", icon: <Key className="w-4 h-4" /> },
  { label: "Platform Settings", href: "/admin/settings", icon: <Settings className="w-4 h-4" /> },
]

export const AdminSidebar: React.FC = () => {
  const location = useLocation()

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

      {/* Divider */}
      <div className="my-4 h-px bg-border-subtle" />

      {/* Management Section */}
      <p className="text-[10px] font-mono font-semibold uppercase tracking-wider text-text-dimmed mb-3 px-3">
        Management
      </p>
      <nav className="space-y-1">
        {managementItems.map((item) => renderNavItem(item, true))}
      </nav>
    </aside>
  )
}

export default AdminSidebar
