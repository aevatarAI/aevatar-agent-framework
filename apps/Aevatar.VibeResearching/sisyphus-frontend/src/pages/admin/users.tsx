import { useState, useEffect } from "react"
import { UserPlus, Mail, MoreHorizontal, Edit2, Trash2, Lock, Eye, ChevronDown, Check } from "lucide-react"
import { AdminLayout } from "@/components/admin"
import { CreateUserModal, EditUserModal, DeleteUserModal, SetPasswordModal, UserDetailModal, InviteUserModal } from "@/components/admin/users"
import { Button } from "@/components/ui/button"
import { SearchInput } from "@/components/ui/input"
import { Table, TableHeader, TableBody, TableHead, TableRow, TableCell } from "@/components/ui/table"
import { Avatar } from "@/components/ui/avatar"
import { Badge, StatusDot } from "@/components/ui/badge"
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator } from "@/components/ui/dropdown-menu"
import { getUsers, getUserStats, createUser, updateUser, deleteUser, setUserPassword, getRoles } from "@/lib/abp"
import type { User, CreateUserInput, UpdateUserInput } from "@/types/user-management"

// ============================================================
//  Users Admin Page - Matches Pencil Design
// ============================================================

export default function UsersPage() {
  const [users, setUsers] = useState<User[]>([])
  const [stats, setStats] = useState({ total: 0, active: 0, roles: 0, admins: 0 })
  const [roles, setRoles] = useState<string[]>([])
  const [search, setSearch] = useState("")
  const [, setIsLoading] = useState(true)

  // Filter states
  const [roleFilter, setRoleFilter] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<string | null>(null)

  // Modal states
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [showInviteModal, setShowInviteModal] = useState(false)
  const [viewingUser, setViewingUser] = useState<User | null>(null)
  const [editingUser, setEditingUser] = useState<User | null>(null)
  const [deletingUser, setDeletingUser] = useState<User | null>(null)
  const [passwordUser, setPasswordUser] = useState<User | null>(null)

  // Load data
  useEffect(() => {
    loadData()
  }, [])

  const loadData = async () => {
    setIsLoading(true)
    try {
      const [usersResult, statsResult, rolesResult] = await Promise.all([
        getUsers({ search }),
        getUserStats(),
        getRoles(),
      ])
      setUsers(usersResult.items)
      // Adapt stats to match design (admins instead of inactive)
      const adminCount = usersResult.items.filter(u => u.roles.includes("admin")).length
      setStats({
        total: statsResult.total,
        active: statsResult.active,
        roles: statsResult.roles,
        admins: adminCount,
      })
      setRoles(rolesResult.map(r => r.name))
    } finally {
      setIsLoading(false)
    }
  }

  // Search handler
  useEffect(() => {
    const timer = setTimeout(() => {
      loadData()
    }, 300)
    return () => clearTimeout(timer)
  }, [search])

  // CRUD handlers
  const handleCreateUser = async (data: CreateUserInput) => {
    await createUser(data)
    loadData()
  }

  const handleUpdateUser = async (data: UpdateUserInput) => {
    if (editingUser) {
      await updateUser(editingUser.id, data)
      loadData()
    }
  }

  const handleDeleteUser = async () => {
    if (deletingUser) {
      await deleteUser(deletingUser.id)
      loadData()
    }
  }

  const handleSetPassword = async (password: string) => {
    if (passwordUser) {
      await setUserPassword(passwordUser.id, password)
    }
  }

  return (
    <AdminLayout
      title="Users"
      subtitle="Manage system users and their permissions"
      actions={
        <div className="flex items-center gap-2.5">
          <Button variant="secondary" onClick={() => setShowInviteModal(true)} className="gap-1.5">
            <Mail className="w-3.5 h-3.5" />
            Invite
          </Button>
          <Button variant="gold" onClick={() => setShowCreateModal(true)} className="gap-1.5">
            <UserPlus className="w-3.5 h-3.5" />
            Add User
          </Button>
        </div>
      }
    >
      {/* Stats Cards - Matching Design */}
      <div className="grid grid-cols-4 gap-4 mb-5">
        {/* Total Users */}
        <div className="rounded-xl bg-surface border border-border-subtle p-4 space-y-1.5">
          <p className="text-[11px] font-mono text-text-dimmed">Total Users</p>
          <span className="text-2xl font-mono font-semibold text-text-primary">
            {stats.total.toLocaleString()}
          </span>
        </div>

        {/* Active Now */}
        <div className="rounded-xl bg-surface border border-border-subtle p-4 space-y-1.5">
          <p className="text-[11px] font-mono text-text-dimmed">Active Now</p>
          <div className="flex items-center gap-2">
            <span className="text-2xl font-mono font-semibold text-neon-cyan">{stats.active}</span>
            <div className="w-2 h-2 rounded-full bg-neon-green" />
          </div>
        </div>

        {/* Roles */}
        <div className="rounded-xl bg-surface border border-border-subtle p-4 space-y-1.5">
          <p className="text-[11px] font-mono text-text-dimmed">Roles</p>
          <span className="text-2xl font-mono font-semibold text-neon-gold">{stats.roles}</span>
        </div>

        {/* Admins */}
        <div className="rounded-xl bg-surface border border-border-subtle p-4 space-y-1.5">
          <p className="text-[11px] font-mono text-text-dimmed">Admins</p>
          <span className="text-2xl font-mono font-semibold text-neon-purple">{stats.admins}</span>
        </div>
      </div>

      {/* Search & Filters - Matching Design */}
      <div className="flex items-center gap-3 mb-5">
        <div className="flex-1">
          <SearchInput
            placeholder="Search users by name or email..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>

        {/* Role Filter Dropdown */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button className="flex items-center gap-1.5 px-3 py-2 text-xs text-text-secondary bg-surface border border-border-subtle rounded-lg hover:border-border-default">
              <span className="capitalize">{roleFilter || "All Roles"}</span>
              <ChevronDown className="w-3.5 h-3.5 text-text-dimmed" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="min-w-[140px]">
            <DropdownMenuItem onClick={() => setRoleFilter(null)}>
              <span className="flex-1">All Roles</span>
              {roleFilter === null && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            {roles.map(role => (
              <DropdownMenuItem key={role} onClick={() => setRoleFilter(role)}>
                <span className="flex-1 capitalize">{role}</span>
                {roleFilter === role && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>

        {/* Status Filter Dropdown */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button className="flex items-center gap-1.5 px-3 py-2 text-xs text-text-secondary bg-surface border border-border-subtle rounded-lg hover:border-border-default">
              {statusFilter ? (statusFilter === "active" ? "Active" : "Inactive") : "All Status"}
              <ChevronDown className="w-3.5 h-3.5 text-text-dimmed" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="min-w-[140px]">
            <DropdownMenuItem onClick={() => setStatusFilter(null)}>
              <span className="flex-1">All Status</span>
              {statusFilter === null && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem onClick={() => setStatusFilter("active")}>
              <span className="flex-1">Active</span>
              {statusFilter === "active" && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => setStatusFilter("inactive")}>
              <span className="flex-1">Inactive</span>
              {statusFilter === "inactive" && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {/* Users Table - Matching Design: USER, EMAIL, ROLE, STATUS, ACTIONS */}
      <div className="border border-border-subtle rounded-xl overflow-hidden bg-surface">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-8">
                <div className="w-4 h-4 rounded border-2 border-border-default" />
              </TableHead>
              <TableHead className="w-[200px]">USER</TableHead>
              <TableHead>EMAIL</TableHead>
              <TableHead className="w-[100px]">ROLE</TableHead>
              <TableHead className="w-[80px]">STATUS</TableHead>
              <TableHead className="w-[60px]">ACTIONS</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {users
              .filter(user => !roleFilter || user.roles.includes(roleFilter))
              .filter(user => !statusFilter || (statusFilter === "active" ? user.isActive : !user.isActive))
              .map((user) => (
              <TableRow key={user.id}>
                {/* Checkbox */}
                <TableCell>
                  <div className="w-4 h-4 rounded border-2 border-border-default" />
                </TableCell>

                {/* USER - Avatar + Name */}
                <TableCell>
                  {(() => {
                    // Build display name with fallbacks
                    const fullName = [user.name, user.surname].filter(Boolean).join(' ').trim()
                    const displayName = fullName || user.userName || user.email.split('@')[0]
                    return (
                      <div className="flex items-center gap-2.5">
                        <Avatar name={displayName} size="sm" />
                        <span className="text-sm text-text-primary">
                          {displayName}
                        </span>
                      </div>
                    )
                  })()}
                </TableCell>

                {/* EMAIL */}
                <TableCell className="text-sm text-text-secondary">
                  {user.email}
                </TableCell>

                {/* ROLE - Single badge */}
                <TableCell>
                  {user.roles.length > 0 ? (
                    <Badge
                      variant={
                        user.roles.includes("admin")
                          ? "gold"
                          : user.roles.includes("member")
                          ? "cyan"
                          : "default"
                      }
                    >
                      {user.roles[0]}
                    </Badge>
                  ) : (
                    <span className="text-xs text-text-dimmed">—</span>
                  )}
                </TableCell>

                {/* STATUS */}
                <TableCell>
                  <StatusDot
                    status={user.isActive ? "active" : "inactive"}
                    label={user.isActive ? "Active" : "Inactive"}
                  />
                </TableCell>

                {/* ACTIONS */}
                <TableCell>
                  <DropdownMenu>
                    <DropdownMenuTrigger>
                      <button className="p-1.5 rounded-md hover:bg-surface-elevated text-text-muted hover:text-text-primary">
                        <MoreHorizontal className="w-4 h-4" />
                      </button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem onClick={() => setViewingUser(user)} icon={<Eye className="w-4 h-4" />}>
                        View Details
                      </DropdownMenuItem>
                      <DropdownMenuItem onClick={() => setEditingUser(user)} icon={<Edit2 className="w-4 h-4" />}>
                        Edit User
                      </DropdownMenuItem>
                      <DropdownMenuItem onClick={() => setPasswordUser(user)} icon={<Lock className="w-4 h-4" />}>
                        Set Password
                      </DropdownMenuItem>
                      <DropdownMenuSeparator />
                      <DropdownMenuItem
                        onClick={() => setDeletingUser(user)}
                        destructive
                        icon={<Trash2 className="w-4 h-4" />}
                      >
                        Delete User
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {/* Modals */}
      <UserDetailModal
        open={!!viewingUser}
        onClose={() => setViewingUser(null)}
        onEdit={() => setEditingUser(viewingUser)}
        user={viewingUser}
      />

      <CreateUserModal
        open={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        onSubmit={handleCreateUser}
        roles={roles}
      />

      <EditUserModal
        open={!!editingUser}
        onClose={() => setEditingUser(null)}
        onSubmit={handleUpdateUser}
        user={editingUser}
        roles={roles}
      />

      <DeleteUserModal
        open={!!deletingUser}
        onClose={() => setDeletingUser(null)}
        onConfirm={handleDeleteUser}
        user={deletingUser}
      />

      <SetPasswordModal
        open={!!passwordUser}
        onClose={() => setPasswordUser(null)}
        onSubmit={handleSetPassword}
        user={passwordUser}
      />

      <InviteUserModal
        open={showInviteModal}
        onClose={() => setShowInviteModal(false)}
        roles={roles}
      />
    </AdminLayout>
  )
}
