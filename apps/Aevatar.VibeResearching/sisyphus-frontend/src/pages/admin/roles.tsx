import { useState, useEffect } from "react"
import { Plus, Shield, MoreHorizontal, Edit2, Trash2, Key, Users } from "lucide-react"
import { AdminLayout } from "@/components/admin"
import { CreateRoleModal, EditRoleModal, DeleteRoleModal, RolePermissionsModal } from "@/components/admin/roles"
import { Button } from "@/components/ui/button"
import { HoverCard } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator } from "@/components/ui/dropdown-menu"
import { getRoles, createRole, updateRole, deleteRole } from "@/lib/mock/roles"
import { getPermissions, updatePermissions } from "@/lib/mock/permissions"
import type { Role, CreateRoleInput, UpdateRoleInput, PermissionGroup } from "@/types/user-management"
import { cn } from "@/lib/utils"

// ============================================================
//  Roles Admin Page
// ============================================================

export default function RolesPage() {
  const [roles, setRoles] = useState<Role[]>([])
  const [, setIsLoading] = useState(true)

  // Modal states
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [editingRole, setEditingRole] = useState<Role | null>(null)
  const [deletingRole, setDeletingRole] = useState<Role | null>(null)
  const [permissionsRole, setPermissionsRole] = useState<Role | null>(null)
  const [permissionGroups, setPermissionGroups] = useState<PermissionGroup[]>([])

  // Load data
  useEffect(() => {
    loadRoles()
  }, [])

  const loadRoles = async () => {
    setIsLoading(true)
    try {
      const result = await getRoles()
      setRoles(result)
    } finally {
      setIsLoading(false)
    }
  }

  // Load permissions when opening permissions modal
  useEffect(() => {
    if (permissionsRole) {
      loadPermissions(permissionsRole.name)
    }
  }, [permissionsRole])

  const loadPermissions = async (roleName: string) => {
    const groups = await getPermissions("R", roleName)
    setPermissionGroups(groups)
  }

  // CRUD handlers
  const handleCreateRole = async (data: CreateRoleInput) => {
    await createRole(data)
    loadRoles()
  }

  const handleUpdateRole = async (data: UpdateRoleInput) => {
    if (editingRole) {
      await updateRole(editingRole.id, data)
      loadRoles()
    }
  }

  const handleDeleteRole = async () => {
    if (deletingRole) {
      await deleteRole(deletingRole.id)
      loadRoles()
    }
  }

  const handleSavePermissions = async (permissions: { name: string; isGranted: boolean }[]) => {
    if (permissionsRole) {
      await updatePermissions("R", permissionsRole.name, permissions)
      loadRoles()
    }
  }

  return (
    <AdminLayout
      title="Roles"
      subtitle="Manage roles and their permissions"
      actions={
        <Button variant="gold" onClick={() => setShowCreateModal(true)} className="gap-1.5">
          <Plus className="w-4 h-4" />
          Create Role
        </Button>
      }
    >
      {/* Roles Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {roles.map((role) => (
          <HoverCard key={role.id} className="p-5">
            {/* Header */}
            <div className="flex items-start justify-between mb-4">
              <div className="flex items-center gap-3">
                <div className={cn(
                  "w-10 h-10 rounded-lg flex items-center justify-center",
                  role.isStatic
                    ? "bg-neon-gold/15 border border-neon-gold/40"
                    : "bg-neon-cyan/15 border border-neon-cyan/40"
                )}>
                  <Shield className={cn(
                    "w-5 h-5",
                    role.isStatic ? "text-neon-gold" : "text-neon-cyan"
                  )} />
                </div>
                <div>
                  <h3 className="font-semibold text-text-primary">{role.name}</h3>
                  <div className="flex items-center gap-2 mt-0.5">
                    {role.isDefault && (
                      <Badge variant="cyan" className="text-[10px] px-1.5 py-0.5">Default</Badge>
                    )}
                    {role.isStatic && (
                      <Badge variant="gold" className="text-[10px] px-1.5 py-0.5">Static</Badge>
                    )}
                  </div>
                </div>
              </div>

              <DropdownMenu>
                <DropdownMenuTrigger>
                  <button className="p-1.5 rounded-md hover:bg-surface-elevated text-text-muted hover:text-text-primary">
                    <MoreHorizontal className="w-4 h-4" />
                  </button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem onClick={() => setEditingRole(role)} icon={<Edit2 className="w-4 h-4" />}>
                    Edit Role
                  </DropdownMenuItem>
                  <DropdownMenuItem onClick={() => setPermissionsRole(role)} icon={<Key className="w-4 h-4" />}>
                    Manage Permissions
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem
                    onClick={() => setDeletingRole(role)}
                    destructive
                    disabled={role.isStatic}
                    icon={<Trash2 className="w-4 h-4" />}
                  >
                    Delete Role
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>

            {/* Description */}
            {role.description && (
              <p className="text-xs text-text-muted mb-4">{role.description}</p>
            )}

            {/* Stats */}
            <div className="grid grid-cols-2 gap-3 pt-3 border-t border-border-subtle">
              <div className="flex items-center gap-2 text-sm">
                <Users className="w-4 h-4 text-text-muted" />
                <span className="text-text-muted">{role.userCount} Users</span>
              </div>
              <div className="flex items-center gap-2 text-sm">
                <Key className="w-4 h-4 text-text-muted" />
                <span className="text-text-muted">{role.permissionCount} Permissions</span>
              </div>
            </div>
          </HoverCard>
        ))}
      </div>

      {/* Modals */}
      <CreateRoleModal
        open={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        onSubmit={handleCreateRole}
      />

      <EditRoleModal
        open={!!editingRole}
        onClose={() => setEditingRole(null)}
        onSubmit={handleUpdateRole}
        role={editingRole}
      />

      <DeleteRoleModal
        open={!!deletingRole}
        onClose={() => setDeletingRole(null)}
        onConfirm={handleDeleteRole}
        role={deletingRole}
      />

      <RolePermissionsModal
        open={!!permissionsRole}
        onClose={() => setPermissionsRole(null)}
        onSave={handleSavePermissions}
        role={permissionsRole}
        permissionGroups={permissionGroups}
      />
    </AdminLayout>
  )
}
