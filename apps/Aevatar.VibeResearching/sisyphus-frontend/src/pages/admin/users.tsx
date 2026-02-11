import { useState, useEffect } from "react"
import { UserPlus, MoreHorizontal, Edit2, Trash2, Lock, Eye, ChevronDown, Check } from "lucide-react"
import { AdminLayout } from "@/components/admin"
import { CreateUserModal, EditUserModal, DeleteUserModal, SetPasswordModal, UserDetailModal } from "@/components/admin/users"
import { Button } from "@/components/ui/button"
import { SearchInput } from "@/components/ui/input"
import { Table, TableHeader, TableBody, TableHead, TableRow, TableCell } from "@/components/ui/table"
import { Pagination } from "@/components/ui/pagination"
import { Avatar } from "@/components/ui/avatar"
import { Badge, StatusDot } from "@/components/ui/badge"
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator } from "@/components/ui/dropdown-menu"
import { getUsers, getUserStats, createUser, updateUser, deleteUser, setUserPassword, getAssignableRoles, invalidateStatsCache } from "@/lib/abp"
import { useToast } from "@/components/ui/toast"
import { usePermission } from "@/hooks/use-permission"
import type { User, CreateUserInput, UpdateUserInput } from "@/types/user-management"

// ============================================================
//  Users Admin Page - Matches Pencil Design
// ============================================================

// 默认 admin 账号不可删除 —— 系统的最后一把钥匙不能被丢掉
const isDefaultAdmin = (user: User) => user.userName === "admin"

export default function UsersPage() {
  const { error: showError, success: showSuccess } = useToast()
  const { isAdmin, hasPermission } = usePermission()

  // 细粒度权限标志 — admin 绕过所有检查，普通用户按 ABP 权限树控制
  const canCreate = isAdmin || hasPermission("AbpIdentity.Users.Create")
  const canUpdate = isAdmin || hasPermission("AbpIdentity.Users.Update")
  const canDelete = isAdmin || hasPermission("AbpIdentity.Users.Delete")

  const [users, setUsers] = useState<User[]>([])
  const [stats, setStats] = useState({ total: 0, active: 0, roles: 0, admins: 0 })
  const [roles, setRoles] = useState<string[]>([])
  const [search, setSearch] = useState("")
  const [, setIsLoading] = useState(true)

  // Pagination state
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [totalItems, setTotalItems] = useState(0)

  // Filter states
  const [roleFilter, setRoleFilter] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<string | null>(null)

  // Selection state
  const [selectedUsers, setSelectedUsers] = useState<Set<string>>(new Set())

  // Modal states
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [viewingUser, setViewingUser] = useState<User | null>(null)
  const [editingUser, setEditingUser] = useState<User | null>(null)
  const [deletingUser, setDeletingUser] = useState<User | null>(null)
  const [passwordUser, setPasswordUser] = useState<User | null>(null)
  const [showBulkDeleteModal, setShowBulkDeleteModal] = useState(false)
  const [isBulkDeleting, setIsBulkDeleting] = useState(false)

  // 是否需要角色列表 — 只有能 Create/Edit 时才加载，避免无权限的 API 调用
  const needsRoles = canCreate || canUpdate

  // Load data and clear selection on page/filter change
  useEffect(() => {
    setSelectedUsers(new Set())
    loadData()
  }, [currentPage, pageSize, roleFilter, statusFilter])

  const loadData = async () => {
    setIsLoading(true)
    try {
      // 核心数据：用户列表（必须）+ 统计/角色（可选，容错降级）
      const [usersResult, statsResult, roleNames] = await Promise.all([
        getUsers({ 
          search,
          skip: (currentPage - 1) * pageSize,
          take: pageSize,
          role: roleFilter || undefined,
          status: statusFilter as 'active' | 'inactive' | undefined,
        }),
        getUserStats().catch(() => ({ total: 0, active: 0, roles: 0, inactive: 0, admins: 0 })),
        needsRoles
          ? getAssignableRoles().catch(() => [] as string[])
          : Promise.resolve([] as string[]),
      ])
      setUsers(usersResult.items)
      setTotalItems(usersResult.totalCount)
      setStats({
        total: statsResult.total,
        active: statsResult.active,
        roles: statsResult.roles,
        admins: statsResult.admins || 0,
      })
      setRoles(roleNames)
    } finally {
      setIsLoading(false)
    }
  }

  // Search handler - reset to page 1 when searching
  useEffect(() => {
    const timer = setTimeout(() => {
      if (currentPage !== 1) {
        setCurrentPage(1)
      } else {
        loadData()
      }
    }, 300)
    return () => clearTimeout(timer)
  }, [search])

  // CRUD handlers
  const handleCreateUser = async (data: CreateUserInput) => {
    try {
      await createUser(data)
      invalidateStatsCache() // Refresh stats on next load
      showSuccess("User created successfully")
      loadData()
    } catch (err) {
      showError("Failed to create user", (err as Error)?.message || "Please try again")
      throw err // Re-throw to keep modal open
    }
  }

  const handleUpdateUser = async (data: UpdateUserInput) => {
    if (!editingUser) return
    // 防御纵深：默认 admin 必须保留 admin 角色且保持激活
    if (isDefaultAdmin(editingUser)) {
      if (!data.roleNames.some(r => r.toLowerCase() === "admin")) {
        data = { ...data, roleNames: [...data.roleNames, "admin"] }
      }
      if (!data.isActive) {
        data = { ...data, isActive: true }
      }
    }
    try {
      await updateUser(editingUser.id, data)
      invalidateStatsCache() // Refresh stats on next load (role might have changed)
      showSuccess("User updated successfully")
      loadData()
    } catch (err) {
      showError("Failed to update user", (err as Error)?.message || "Please try again")
      throw err
    }
  }

  const handleDeleteUser = async () => {
    if (!deletingUser) return
    // 终极防线：即使代码路径被绕过，也绝不删除 admin
    if (isDefaultAdmin(deletingUser)) return
    try {
      await deleteUser(deletingUser.id)
      invalidateStatsCache() // Refresh stats on next load
      showSuccess("User deleted successfully")
      loadData()
    } catch (err) {
      showError("Failed to delete user", (err as Error)?.message || "Please try again")
      throw err
    }
  }

  const handleSetPassword = async (password: string) => {
    if (!passwordUser) return
    try {
      await setUserPassword(passwordUser.id, password)
      showSuccess("Password updated successfully")
    } catch (err) {
      showError("Failed to set password", (err as Error)?.message || "Please try again")
      throw err
    }
  }

  const handleBulkDelete = async () => {
    if (selectedUsers.size === 0) return
    setIsBulkDeleting(true)
    try {
      // 防御性过滤：即使 UI 已阻止，逻辑层也不允许删除 admin
      const adminIds = new Set(users.filter(isDefaultAdmin).map(u => u.id))
      const safeIds = Array.from(selectedUsers).filter(id => !adminIds.has(id))
      if (safeIds.length === 0) return
      // Delete users one by one (ABP doesn't have bulk delete API)
      const deletePromises = safeIds.map(id => deleteUser(id))
      const results = await Promise.allSettled(deletePromises)
      
      const succeeded = results.filter(r => r.status === 'fulfilled').length
      const failed = results.filter(r => r.status === 'rejected').length
      
      if (failed === 0) {
        showSuccess(`Successfully deleted ${succeeded} user(s)`)
      } else {
        showError(`Deleted ${succeeded} user(s), but ${failed} failed`)
      }
      
      invalidateStatsCache() // Refresh stats on next load
      setSelectedUsers(new Set())
      setShowBulkDeleteModal(false)
      loadData()
    } catch (err) {
      showError("Bulk delete failed", (err as Error)?.message || "Please try again")
    } finally {
      setIsBulkDeleting(false)
    }
  }

  return (
    <AdminLayout
      title="Users"
      subtitle="Manage system users and their permissions"
      actions={
        canCreate ? (
          <Button variant="gold" onClick={() => setShowCreateModal(true)} className="gap-1.5">
            <UserPlus className="w-3.5 h-3.5" />
            Add User
          </Button>
        ) : undefined
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
            <DropdownMenuItem onClick={() => { setRoleFilter(null); setCurrentPage(1); }}>
              <span className="flex-1">All Roles</span>
              {roleFilter === null && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            {roles.map(role => (
              <DropdownMenuItem key={role} onClick={() => { setRoleFilter(role); setCurrentPage(1); }}>
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
            <DropdownMenuItem onClick={() => { setStatusFilter(null); setCurrentPage(1); }}>
              <span className="flex-1">All Status</span>
              {statusFilter === null && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem onClick={() => { setStatusFilter("active"); setCurrentPage(1); }}>
              <span className="flex-1">Active</span>
              {statusFilter === "active" && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => { setStatusFilter("inactive"); setCurrentPage(1); }}>
              <span className="flex-1">Inactive</span>
              {statusFilter === "inactive" && <Check className="w-3.5 h-3.5 text-neon-cyan" />}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {/* Bulk Action Bar — 仅有删除权限时才显示 */}
      {canDelete && selectedUsers.size > 0 && (
        <div className="flex items-center justify-between px-4 py-3 mb-4 rounded-xl bg-neon-cyan/10 border border-neon-cyan/30">
          <span className="text-sm text-text-primary">
            <span className="font-medium text-neon-cyan">{selectedUsers.size}</span> user(s) selected
          </span>
          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setSelectedUsers(new Set())}
            >
              Clear Selection
            </Button>
            <Button
              variant="destructive"
              size="sm"
              onClick={() => setShowBulkDeleteModal(true)}
              className="gap-1.5"
            >
              <Trash2 className="w-3.5 h-3.5" />
              Delete Selected
            </Button>
          </div>
        </div>
      )}

      {/* Users Table - Matching Design: USER, EMAIL, ROLE, STATUS, ACTIONS */}
      <div className="border border-border-subtle rounded-xl overflow-hidden bg-surface">
        <Table className="table-fixed">
          <TableHeader>
            <TableRow>
              {canDelete && (
              <TableHead className="w-10">
                {(() => {
                  const selectableUsers = users.filter(u => !isDefaultAdmin(u))
                  return (
                    <input
                      type="checkbox"
                      checked={selectableUsers.length > 0 && selectedUsers.size === selectableUsers.length}
                      onChange={(e) => {
                        if (e.target.checked) {
                          setSelectedUsers(new Set(selectableUsers.map(u => u.id)))
                        } else {
                          setSelectedUsers(new Set())
                        }
                      }}
                      className="w-4 h-4 rounded border-2 border-border-default accent-neon-cyan cursor-pointer"
                    />
                  )
                })()}
              </TableHead>
              )}
              <TableHead className="w-[180px]">USER</TableHead>
              <TableHead className="w-[220px]">EMAIL</TableHead>
              <TableHead className="w-[140px]">ROLE</TableHead>
              <TableHead className="w-[80px]">STATUS</TableHead>
              <TableHead className="w-[60px]">ACTIONS</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {users.length === 0 ? (
              <TableRow>
                <TableCell colSpan={canDelete ? 6 : 5} className="h-32 text-center">
                  <div className="flex flex-col items-center justify-center gap-2 text-text-muted">
                    <UserPlus className="w-8 h-8 text-text-dimmed" />
                    <p className="text-sm">No users found</p>
                    {search && (
                      <p className="text-xs text-text-dimmed">
                        Try adjusting your search or filters
                      </p>
                    )}
                  </div>
                </TableCell>
              </TableRow>
            ) : (
              users.map((user) => (
              <TableRow key={user.id} className={selectedUsers.has(user.id) ? "bg-neon-cyan/5" : ""}>
                {/* Checkbox — 仅有删除权限时显示，admin 不可被选中 */}
                {canDelete && (
                <TableCell>
                  {isDefaultAdmin(user) ? (
                    <div className="w-4 h-4" />
                  ) : (
                    <input
                      type="checkbox"
                      checked={selectedUsers.has(user.id)}
                      onChange={(e) => {
                        const newSelected = new Set(selectedUsers)
                        if (e.target.checked) {
                          newSelected.add(user.id)
                        } else {
                          newSelected.delete(user.id)
                        }
                        setSelectedUsers(newSelected)
                      }}
                      className="w-4 h-4 rounded border-2 border-border-default accent-neon-cyan cursor-pointer"
                    />
                  )}
                </TableCell>
                )}

                {/* USER - Avatar + Name */}
                <TableCell>
                  {(() => {
                    // Build display name with fallbacks
                    const fullName = [user.name, user.surname].filter(Boolean).join(' ').trim()
                    const displayName = fullName || user.userName || user.email.split('@')[0]
                    return (
                      <div className="flex items-center gap-2.5 min-w-0">
                        <Avatar name={displayName} src={user.avatarUrl} size="sm" className="flex-shrink-0" />
                        <span className="text-sm text-text-primary truncate" title={displayName}>
                          {displayName}
                        </span>
                      </div>
                    )
                  })()}
                </TableCell>

                {/* EMAIL */}
                <TableCell className="text-sm text-text-secondary truncate" title={user.email}>
                  {user.email}
                </TableCell>

                {/* ROLE - All roles */}
                <TableCell>
                  {user.roles.length > 0 ? (
                    <div className="flex flex-wrap gap-1">
                      {user.roles.map((role) => (
                        <Badge
                          key={role}
                          variant={
                            role.toLowerCase() === "admin"
                              ? "gold"
                              : role.toLowerCase() === "member"
                              ? "cyan"
                              : "default"
                          }
                        >
                          {role}
                        </Badge>
                      ))}
                    </div>
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
                      {canUpdate && (
                        <>
                          <DropdownMenuItem onClick={() => setEditingUser(user)} icon={<Edit2 className="w-4 h-4" />}>
                            Edit User
                          </DropdownMenuItem>
                          <DropdownMenuItem onClick={() => setPasswordUser(user)} icon={<Lock className="w-4 h-4" />}>
                            Set Password
                          </DropdownMenuItem>
                        </>
                      )}
                      {canDelete && !isDefaultAdmin(user) && (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            onClick={() => setDeletingUser(user)}
                            destructive
                            icon={<Trash2 className="w-4 h-4" />}
                          >
                            Delete User
                          </DropdownMenuItem>
                        </>
                      )}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </TableCell>
              </TableRow>
              ))
            )}
          </TableBody>
        </Table>

        {/* Pagination */}
        <Pagination
          currentPage={currentPage}
          totalPages={Math.ceil(totalItems / pageSize)}
          totalItems={totalItems}
          pageSize={pageSize}
          onPageChange={setCurrentPage}
          onPageSizeChange={(size) => {
            setPageSize(size)
            setCurrentPage(1)
          }}
        />
      </div>

      {/* Modals */}
      <UserDetailModal
        open={!!viewingUser}
        onClose={() => setViewingUser(null)}
        onEdit={canUpdate ? () => setEditingUser(viewingUser) : undefined}
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

      {/* Bulk Delete Confirmation Modal */}
      {showBulkDeleteModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="absolute inset-0 bg-bg-base/80 backdrop-blur-sm" onClick={() => !isBulkDeleting && setShowBulkDeleteModal(false)} />
          <div className="relative z-10 w-[400px] rounded-xl bg-surface border border-border-subtle p-6 space-y-4">
            <div className="space-y-2">
              <h3 className="text-lg font-semibold text-text-primary">Confirm Bulk Delete</h3>
              <p className="text-sm text-text-secondary">
                Are you sure you want to delete <span className="font-medium text-neon-red">{selectedUsers.size}</span> user(s)? 
                This action cannot be undone.
              </p>
            </div>
            <div className="flex justify-end gap-3 pt-2">
              <Button
                variant="secondary"
                onClick={() => setShowBulkDeleteModal(false)}
                disabled={isBulkDeleting}
              >
                Cancel
              </Button>
              <Button
                variant="destructive"
                onClick={handleBulkDelete}
                disabled={isBulkDeleting}
                className="gap-1.5"
              >
                {isBulkDeleting ? (
                  <>
                    <div className="w-3.5 h-3.5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                    Deleting...
                  </>
                ) : (
                  <>
                    <Trash2 className="w-3.5 h-3.5" />
                    Delete {selectedUsers.size} User(s)
                  </>
                )}
              </Button>
            </div>
          </div>
        </div>
      )}
    </AdminLayout>
  )
}
