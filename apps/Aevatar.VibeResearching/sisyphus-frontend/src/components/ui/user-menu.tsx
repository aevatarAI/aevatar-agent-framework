import React from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { Shield, User, Users, Key, LogOut, Settings, ChevronDown, Lock } from 'lucide-react'
import { Avatar } from '@/components/ui/avatar'
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuLabel,
} from '@/components/ui/dropdown-menu'
import { useAuthStore, useIsAdmin } from '@/store/auth-store'

// ============================================================
//  User Menu - Reusable Profile Dropdown
// ============================================================

interface UserMenuProps {
  showName?: boolean
}

export const UserMenu: React.FC<UserMenuProps> = ({ showName = true }) => {
  const navigate = useNavigate()
  const { user, isAuthenticated, logout } = useAuthStore()
  const isAdmin = useIsAdmin()

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  if (!isAuthenticated || !user) {
    return (
      <div className="flex items-center gap-2">
        <Link
          to="/register"
          className="flex items-center gap-2 px-3 py-2 rounded-lg border border-neon-cyan/40 text-neon-cyan text-sm font-semibold hover:bg-neon-cyan/10 transition-colors"
        >
          Register
        </Link>
        <Link
          to="/login"
          className="flex items-center gap-2 px-3 py-2 rounded-lg bg-neon-cyan text-bg-base text-sm font-semibold hover:bg-neon-sky transition-colors"
        >
          Sign In
        </Link>
      </div>
    )
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button className="flex items-center gap-2 px-2 py-1.5 rounded-lg hover:bg-surface-elevated transition-colors">
          <Avatar
            name={user.name || user.userName}
            src={user.avatarUrl}
            size="sm"
          />
          {showName && (
            <div className="hidden sm:flex flex-col items-start">
              <span className="text-sm font-medium text-text-primary">
                {user.name || user.userName}{user.surname ? ` ${user.surname}` : ''}
              </span>
              <span className="text-[10px] text-text-muted">
                {isAdmin ? 'Administrator' : 'Member'}
              </span>
            </div>
          )}
          <ChevronDown className="w-4 h-4 text-text-muted" />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        {/* Admin Section */}
        {isAdmin && (
          <>
            <DropdownMenuLabel className="text-neon-gold">
              Administration
            </DropdownMenuLabel>
            <DropdownMenuItem
              onClick={() => navigate('/admin/users')}
              icon={<Users className="w-4 h-4" />}
            >
              Users
            </DropdownMenuItem>
            <DropdownMenuItem
              onClick={() => navigate('/admin/roles')}
              icon={<Shield className="w-4 h-4" />}
            >
              Roles
            </DropdownMenuItem>
            <DropdownMenuItem
              onClick={() => navigate('/admin/permissions')}
              icon={<Key className="w-4 h-4" />}
            >
              Permissions
            </DropdownMenuItem>
            <DropdownMenuSeparator />
          </>
        )}

        {/* Account Section */}
        <DropdownMenuItem
          onClick={() => navigate('/account/profile')}
          icon={<User className="w-4 h-4" />}
        >
          Profile
        </DropdownMenuItem>
        <DropdownMenuItem
          onClick={() => navigate('/account/password')}
          icon={<Lock className="w-4 h-4" />}
        >
          Password
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onClick={() => navigate('/admin/settings')}
          icon={<Settings className="w-4 h-4" />}
        >
          Platform Settings
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onClick={handleLogout}
          destructive
          icon={<LogOut className="w-4 h-4" />}
        >
          Sign Out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

export default UserMenu

