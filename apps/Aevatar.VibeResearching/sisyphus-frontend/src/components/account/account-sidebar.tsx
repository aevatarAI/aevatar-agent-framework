import React from "react"
import { NavLink, useLocation } from "react-router-dom"
import { User, Lock, Users, Shield, Key, Cpu } from "lucide-react"
import { cn } from "@/lib/utils"
import { useHasPassword } from "@/store/auth-store"

// ============================================================
//  Account Sidebar - Navigation Menu (Basic Version)
// ============================================================

interface NavItem {
  label: string
  href: string
  icon: React.ReactNode
}

const adminItems: NavItem[] = [
  { label: "Users", href: "/admin/users", icon: <Users className="w-4 h-4" /> },
  { label: "Roles", href: "/admin/roles", icon: <Shield className="w-4 h-4" /> },
  { label: "Permissions", href: "/admin/permissions", icon: <Key className="w-4 h-4" /> },
]

interface AccountSidebarProps {
  isAdmin: boolean
}

export const AccountSidebar: React.FC<AccountSidebarProps> = ({ isAdmin }) => {
  const hasPassword = useHasPassword()

  // Build settings items dynamically — hide Password for OAuth users
  const settingsItems: NavItem[] = [
    { label: "Profile", href: "/account/profile", icon: <User className="w-4 h-4" /> },
    ...(hasPassword
      ? [{ label: "Password", href: "/account/password", icon: <Lock className="w-4 h-4" /> }]
      : []),
    { label: "My Providers", href: "/account/providers", icon: <Cpu className="w-4 h-4" /> },
  ]
  const location = useLocation()

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

      {/* Admin Section (only for admins) */}
      {isAdmin && (
        <div className="mt-4 pt-4 border-t border-border-subtle">
          <div className="flex items-center justify-between mb-3 px-3">
            <p className="text-[10px] font-mono font-semibold uppercase tracking-wider text-neon-gold">
              Administration
            </p>
            <span className="px-1.5 py-0.5 text-[8px] font-mono font-semibold text-neon-gold bg-neon-gold/20 rounded">
              Admin
            </span>
          </div>
          <nav className="space-y-1">
            {adminItems.map((item) => renderNavItem(item, true))}
          </nav>
        </div>
      )}
    </aside>
  )
}

export default AccountSidebar
