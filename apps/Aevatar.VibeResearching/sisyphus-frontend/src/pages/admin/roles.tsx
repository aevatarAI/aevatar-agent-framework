import { useState, useEffect } from "react"
import { Plus, Shield, MoreHorizontal, Edit2, Trash2, Key } from "lucide-react"
import { AdminLayout } from "@/components/admin"
import { CreateRoleModal, EditRoleModal, DeleteRoleModal, RolePermissionsModal } from "@/components/admin/roles"
import { Button } from "@/components/ui/button"
import { HoverCard } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator } from "@/components/ui/dropdown-menu"
import { getRoles, createRole, updateRole, deleteRole, getPermissions, updatePermissions } from "@/lib/abp"
import type { Role, CreateRoleInput, UpdateRoleInput, PermissionGroup } from "@/types/user-management"
import { cn } from "@/lib/utils"
import { useToast } from "@/components/ui/toast"
import { usePermission } from "@/hooks/use-permission"

// ============================================================
//  Roles Admin Page
// ============================================================

export default function RolesPage() {
  const [roles, setRoles] = useState<Role[]>([])
  const [, setIsLoading] = useState(true)
  const toast = useToast()
  const { hasPermission, isAdmin } = usePermission()

  // Permission-based action visibility
  const canCreate = isAdmin || hasPermission("AbpIdentity.Roles.Create")
  const canUpdate = isAdmin || hasPermission("AbpIdentity.Roles.Update")
  const canDelete = isAdmin || hasPermission("AbpIdentity.Roles.Delete")
  const canManagePermissions = isAdmin || hasPermission("AbpIdentity.Roles.ManagePermissions")
  // Only show "..." dropdown if user has any in-card action (not Create, which is a top-level button)
  const hasDropdownAction = canUpdate || canDelete || canManagePermissions

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

  // Helper to extract error message from ABP error response
  const getErrorMessage = (error: unknown): string => {
    if (error && typeof error === "object" && "message" in error) {
      return (error as { message: string }).message
    }
    return "An unexpected error occurred"
  }

  // CRUD handlers
  const handleCreateRole = async (data: CreateRoleInput) => {
    try {
      await createRole(data)
      toast.success("Role Created", `Role "${data.name}" has been created successfully`)
      loadRoles()
    } catch (error) {
      toast.error("Failed to Create Role", getErrorMessage(error))
      throw error // Re-throw to prevent modal from closing
    }
  }

  const handleUpdateRole = async (data: UpdateRoleInput) => {
    if (editingRole) {
      try {
        await updateRole(editingRole.id, data)
        toast.success("Role Updated", `Role "${data.name}" has been updated successfully`)
        loadRoles()
      } catch (error) {
        toast.error("Failed to Update Role", getErrorMessage(error))
        throw error
      }
    }
  }

  const handleDeleteRole = async () => {
    if (deletingRole) {
      try {
        await deleteRole(deletingRole.id)
        toast.success("Role Deleted", `Role "${deletingRole.name}" has been deleted`)
        loadRoles()
      } catch (error) {
        toast.error("Failed to Delete Role", getErrorMessage(error))
        throw error
      }
    }
  }

  const handleSavePermissions = async (permissions: { name: string; isGranted: boolean }[]) => {
    if (permissionsRole) {
      try {
        await updatePermissions("R", permissionsRole.name, permissions)
        toast.success("Permissions Updated", `Permissions for "${permissionsRole.name}" have been saved`)
        loadRoles()
      } catch (error) {
        toast.error("Failed to Update Permissions", getErrorMessage(error))
        throw error
      }
    }
  }

  return (
    <AdminLayout
      title="Roles"
      subtitle="Manage roles and their permissions"
      actions={
        canCreate ? (
          <Button variant="gold" onClick={() => setShowCreateModal(true)} className="gap-1.5">
            <Plus className="w-4 h-4" />
            Create Role
          </Button>
        ) : undefined
      }
    >
      {/* Roles Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {roles.map((role) => (
          <HoverCard key={role.id} className="p-5">
            {/* Header */}
            <div className="flex items-start justify-between gap-2 mb-4 overflow-hidden">
              <div className="flex items-center gap-3 min-w-0 flex-1">
                <div className={cn(
                  "w-10 h-10 rounded-lg flex items-center justify-center flex-shrink-0",
                  role.isStatic
                    ? "bg-neon-gold/15 border border-neon-gold/40"
                    : "bg-neon-cyan/15 border border-neon-cyan/40"
                )}>
                  <Shield className={cn(
                    "w-5 h-5",
                    role.isStatic ? "text-neon-gold" : "text-neon-cyan"
                  )} />
                </div>
                <div className="min-w-0 flex-1">
                  <h3 className="font-semibold text-text-primary truncate" title={role.name}>{role.name}</h3>
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

              {hasDropdownAction && (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <button className="p-1.5 rounded-md hover:bg-surface-elevated text-text-muted hover:text-text-primary">
                      <MoreHorizontal className="w-4 h-4" />
                    </button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    {canUpdate && (
                      <DropdownMenuItem onClick={() => setEditingRole(role)} icon={<Edit2 className="w-4 h-4" />}>
                        Edit Role
                      </DropdownMenuItem>
                    )}
                    {canManagePermissions && (
                      <DropdownMenuItem onClick={() => setPermissionsRole(role)} icon={<Key className="w-4 h-4" />}>
                        Manage Permissions
                      </DropdownMenuItem>
                    )}
                    {canDelete && (
                      <>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                          onClick={() => setDeletingRole(role)}
                          destructive
                          disabled={role.isStatic}
                          icon={<Trash2 className="w-4 h-4" />}
                        >
                          Delete Role
                        </DropdownMenuItem>
                      </>
                    )}
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
            </div>

            {/* Description */}
            {role.description && (
              <p className="text-xs text-text-muted mb-4">{role.description}</p>
            )}

            {/* Stats */}
            <div className="pt-3 border-t border-border-subtle">
              <div className="flex items-center gap-2 text-sm">
                <Key className="w-4 h-4 text-text-muted" />
                <span className="text-text-muted">
                  {role.permissionCount} Permissions
                </span>
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
