import React, { useState, useEffect } from "react"
import { X, Shield, ShieldPlus, ShieldX, Save, Trash2, TriangleAlert } from "lucide-react"
import { Dialog, DialogContent } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input, SearchInput } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import type { Role, CreateRoleInput, UpdateRoleInput, PermissionGroup } from "@/types/user-management"
import { cn } from "@/lib/utils"

// ============================================================
//  Create Role Modal
// ============================================================

interface CreateRoleModalProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: CreateRoleInput) => Promise<void>
}

export const CreateRoleModal: React.FC<CreateRoleModalProps> = ({
  open,
  onClose,
  onSubmit,
}) => {
  const [isLoading, setIsLoading] = useState(false)
  const [formData, setFormData] = useState({
    name: "",
    description: "",
    isDefault: false,
    isPublic: true,
  })

  // Reset form when modal opens
  useEffect(() => {
    if (open) {
      setFormData({ name: "", description: "", isDefault: false, isPublic: true })
    }
  }, [open])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)
    try {
      await onSubmit({
        name: formData.name,
        isDefault: formData.isDefault,
        isPublic: formData.isPublic,
      })
      onClose()
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[480px] p-0">
        <form onSubmit={handleSubmit}>
          {/* Header */}
          <div className="flex items-center justify-between px-6 py-5 border-b border-border-subtle">
            <div className="space-y-1">
              <h2 className="text-lg font-semibold text-text-primary">Create Role</h2>
              <p className="text-[13px] text-text-muted">Define a new role with permissions</p>
            </div>
            <button type="button" onClick={onClose} className="w-8 h-8 rounded-lg bg-surface flex items-center justify-center text-text-secondary hover:text-text-primary">
              <X className="w-4 h-4" />
            </button>
          </div>

          {/* Body */}
          <div className="px-6 py-5 space-y-5">
            {/* Role Name */}
            <div className="space-y-1.5">
              <label className="text-[13px] font-medium text-text-secondary">Role Name *</label>
              <Input
                value={formData.name}
                onChange={(e) => setFormData(prev => ({ ...prev, name: e.target.value }))}
                placeholder="Enter role name"
                required
              />
            </div>

            {/* Description */}
            <div className="space-y-1.5">
              <label className="text-[13px] font-medium text-text-secondary">Description</label>
              <textarea
                value={formData.description}
                onChange={(e) => setFormData(prev => ({ ...prev, description: e.target.value }))}
                placeholder="Describe this role's purpose"
                className="w-full h-20 px-3 py-2.5 rounded-lg bg-surface border border-border-subtle text-[13px] text-text-primary placeholder:text-text-dimmed resize-none focus:outline-none focus:border-neon-cyan/50"
              />
            </div>

            {/* Default Role Switch */}
            <div className="flex items-center justify-between pt-4 border-t border-border-subtle">
              <div className="space-y-0.5">
                <label className="text-sm font-medium text-text-primary">Default Role</label>
                <p className="text-xs text-text-muted">Automatically assign to new users</p>
              </div>
              <Switch
                checked={formData.isDefault}
                onCheckedChange={(checked) => setFormData(prev => ({ ...prev, isDefault: checked }))}
              />
            </div>

            {/* Public Role Switch */}
            <div className="flex items-center justify-between pt-4 border-t border-border-subtle">
              <div className="space-y-0.5">
                <label className="text-sm font-medium text-text-primary">Public Role</label>
                <p className="text-xs text-text-muted">Users can request to join this role</p>
              </div>
              <Switch
                checked={formData.isPublic}
                onCheckedChange={(checked) => setFormData(prev => ({ ...prev, isPublic: checked }))}
              />
            </div>
          </div>

          {/* Footer */}
          <div className="flex justify-end gap-3 px-6 py-4 border-t border-border-subtle">
            <Button type="button" variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" variant="gold" disabled={isLoading || !formData.name} className="gap-1.5">
              <ShieldPlus className="w-3.5 h-3.5" />
              {isLoading ? "Creating..." : "Create Role"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Edit Role Modal
// ============================================================

interface EditRoleModalProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: UpdateRoleInput) => Promise<void>
  role: Role | null
}

export const EditRoleModal: React.FC<EditRoleModalProps> = ({
  open,
  onClose,
  onSubmit,
  role,
}) => {
  const [isLoading, setIsLoading] = useState(false)
  const [formData, setFormData] = useState({
    name: "",
    description: "",
    isDefault: false,
    isPublic: true,
  })

  useEffect(() => {
    if (role) {
      setFormData({
        name: role.name,
        description: role.description || "",
        isDefault: role.isDefault,
        isPublic: role.isPublic,
      })
    }
  }, [role])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)
    try {
      await onSubmit({
        name: formData.name,
        isDefault: formData.isDefault,
        isPublic: formData.isPublic,
      })
      onClose()
    } finally {
      setIsLoading(false)
    }
  }

  if (!role) return null

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[480px] p-0">
        <form onSubmit={handleSubmit}>
          {/* Header */}
          <div className="flex items-center justify-between px-6 py-5 border-b border-border-subtle">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-[10px] bg-neon-purple/20 flex items-center justify-center">
                <Shield className="w-5 h-5 text-neon-purple" />
              </div>
              <div className="space-y-0.5">
                <h2 className="text-lg font-semibold text-text-primary">Edit Role</h2>
                <p className="text-xs text-text-muted">Modify role settings</p>
              </div>
            </div>
            <button type="button" onClick={onClose} className="w-8 h-8 rounded-lg bg-surface flex items-center justify-center text-text-secondary hover:text-text-primary">
              <X className="w-4 h-4" />
            </button>
          </div>

          {/* Body */}
          <div className="px-6 py-5 space-y-5">
            {/* Role Name */}
            <div className="space-y-1.5">
              <label className="text-[13px] font-medium text-text-secondary">Role Name *</label>
              <Input
                value={formData.name}
                onChange={(e) => setFormData(prev => ({ ...prev, name: e.target.value }))}
                disabled={role.isStatic}
              />
              {role.isStatic && (
                <p className="text-xs text-text-dimmed">Static roles cannot be renamed</p>
              )}
            </div>

            {/* Description */}
            <div className="space-y-1.5">
              <label className="text-[13px] font-medium text-text-secondary">Description</label>
              <textarea
                value={formData.description}
                onChange={(e) => setFormData(prev => ({ ...prev, description: e.target.value }))}
                placeholder="Describe this role's purpose"
                className="w-full h-20 px-3 py-2.5 rounded-lg bg-surface border border-border-subtle text-[13px] text-text-primary placeholder:text-text-dimmed resize-none focus:outline-none focus:border-neon-cyan/50"
              />
            </div>

            {/* Default Role Switch */}
            <div className="flex items-center justify-between pt-4 border-t border-border-subtle">
              <div className="space-y-0.5">
                <label className="text-sm font-medium text-text-primary">Default Role</label>
                <p className="text-xs text-text-muted">Automatically assign to new users</p>
              </div>
              <Switch
                checked={formData.isDefault}
                onCheckedChange={(checked) => setFormData(prev => ({ ...prev, isDefault: checked }))}
              />
            </div>

            {/* Public Role Switch */}
            <div className="flex items-center justify-between pt-4 border-t border-border-subtle">
              <div className="space-y-0.5">
                <label className="text-sm font-medium text-text-primary">Public Role</label>
                <p className="text-xs text-text-muted">Users can request to join this role</p>
              </div>
              <Switch
                checked={formData.isPublic}
                onCheckedChange={(checked) => setFormData(prev => ({ ...prev, isPublic: checked }))}
              />
            </div>
          </div>

          {/* Footer */}
          <div className="flex justify-end gap-3 px-6 py-4 border-t border-border-subtle">
            <Button type="button" variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" variant="purple" disabled={isLoading} className="gap-1.5">
              <Save className="w-3.5 h-3.5" />
              {isLoading ? "Saving..." : "Save Changes"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Delete Role Modal
// ============================================================

interface DeleteRoleModalProps {
  open: boolean
  onClose: () => void
  onConfirm: () => Promise<void>
  role: Role | null
}

export const DeleteRoleModal: React.FC<DeleteRoleModalProps> = ({
  open,
  onClose,
  onConfirm,
  role,
}) => {
  const [isLoading, setIsLoading] = useState(false)

  const handleConfirm = async () => {
    setIsLoading(true)
    try {
      await onConfirm()
      onClose()
    } finally {
      setIsLoading(false)
    }
  }

  if (!role) return null

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[420px] p-0">
        {/* Content */}
        <div className="px-6 py-6 flex flex-col items-center text-center">
          {/* Icon */}
          <div className="w-16 h-16 rounded-full bg-neon-red/10 border border-neon-red/30 flex items-center justify-center mb-5">
            <ShieldX className="w-7 h-7 text-neon-red" />
          </div>
          
          {/* Text */}
          <h2 className="text-xl font-semibold text-text-primary mb-2">Delete Role?</h2>
          <p className="text-sm text-text-muted mb-3">
            Are you sure you want to delete the role
          </p>
          
          {/* Role Name Pill */}
          <div className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-neon-purple/10 mb-5">
            <Shield className="w-3.5 h-3.5 text-neon-purple" />
            <span className="text-[13px] font-medium text-neon-purple capitalize">{role.name}</span>
          </div>
          
          {/* Warning Box */}
          {role.userCount > 0 && (
            <div className="w-full p-3 rounded-lg bg-neon-red/10 border border-neon-red/30 text-left space-y-1.5">
              <div className="flex items-center gap-2">
                <TriangleAlert className="w-3.5 h-3.5 text-neon-red" />
                <span className="text-xs font-semibold text-neon-red">Warning</span>
              </div>
              <p className="text-xs text-text-muted">
                {role.userCount} users will lose their assigned permissions.
              </p>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex gap-3 px-6 py-4 border-t border-border-subtle">
          <Button variant="secondary" className="flex-1 h-[42px]" onClick={onClose}>
            Cancel
          </Button>
          <Button
            variant="destructive"
            className="flex-1 h-[42px] gap-1.5"
            onClick={handleConfirm}
            disabled={isLoading || role.isStatic}
          >
            <Trash2 className="w-4 h-4" />
            {isLoading ? "Deleting..." : "Delete Role"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Role Permissions Modal
// ============================================================

interface RolePermissionsModalProps {
  open: boolean
  onClose: () => void
  onSave: (permissions: { name: string; isGranted: boolean }[]) => Promise<void>
  role: Role | null
  permissionGroups: PermissionGroup[]
}

export const RolePermissionsModal: React.FC<RolePermissionsModalProps> = ({
  open,
  onClose,
  onSave,
  role,
  permissionGroups,
}) => {
  const [isLoading, setIsLoading] = useState(false)
  const [search, setSearch] = useState("")
  const [permissions, setPermissions] = useState<Record<string, boolean>>({})

  // Build permission hierarchy maps
  const allPermissions = permissionGroups.flatMap(g => g.permissions)
  const childrenMap = new Map<string, string[]>() // parent -> children
  
  allPermissions.forEach(perm => {
    if (perm.parentName) {
      const children = childrenMap.get(perm.parentName) || []
      children.push(perm.name)
      childrenMap.set(perm.parentName, children)
    }
  })

  useEffect(() => {
    const initial: Record<string, boolean> = {}
    permissionGroups.forEach(group => {
      group.permissions.forEach(perm => {
        initial[perm.name] = perm.isGranted
      })
    })
    setPermissions(initial)
  }, [permissionGroups])

  // Get all descendants recursively
  const getDescendants = (name: string): string[] => {
    const children = childrenMap.get(name) || []
    return children.flatMap(child => [child, ...getDescendants(child)])
  }

  // Find parent permission
  const findParent = (name: string): string | undefined => {
    return allPermissions.find(p => p.name === name)?.parentName
  }

  const togglePermission = (name: string) => {
    setPermissions(prev => {
      const newValue = !prev[name]
      const updates: Record<string, boolean> = { [name]: newValue }

      if (newValue) {
        // Granting: auto-grant all ancestors (parent chain)
        let parent = findParent(name)
        while (parent) {
          updates[parent] = true
          parent = findParent(parent)
        }
      } else {
        // Revoking: auto-revoke all descendants
        const descendants = getDescendants(name)
        descendants.forEach(d => {
          updates[d] = false
        })
      }

      return { ...prev, ...updates }
    })
  }

  const toggleGroup = (groupName: string, value: boolean) => {
    const group = permissionGroups.find(g => g.name === groupName)
    if (group) {
      const updates: Record<string, boolean> = {}
      group.permissions.forEach(p => {
        updates[p.name] = value
      })
      setPermissions(prev => ({ ...prev, ...updates }))
    }
  }

  const handleSave = async () => {
    setIsLoading(true)
    try {
      const updates = Object.entries(permissions).map(([name, isGranted]) => ({
        name,
        isGranted,
      }))
      await onSave(updates)
      onClose()
    } finally {
      setIsLoading(false)
    }
  }

  // Filter permissions by allowedProviders for Roles ("R")
  // NOTE: Empty allowedProviders array means permission is allowed for ALL provider types
  const filterByProvider = (perms: typeof allPermissions) =>
    perms.filter(p => p.allowedProviders.length === 0 || p.allowedProviders.includes("R"))

  // Count granted permissions (only those allowed for Roles)
  const allowedPermissionNames = new Set(filterByProvider(allPermissions).map(p => p.name))
  const grantedCount = Object.entries(permissions)
    .filter(([name, granted]) => granted && allowedPermissionNames.has(name))
    .length

  // Filter groups by search and allowedProviders
  const filteredGroups = permissionGroups
    .map(group => ({
      ...group,
      permissions: filterByProvider(group.permissions).filter(p =>
        search === "" ||
        p.displayName.toLowerCase().includes(search.toLowerCase()) ||
        group.displayName.toLowerCase().includes(search.toLowerCase())
      )
    }))
    .filter(group => group.permissions.length > 0)

  if (!role) return null

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[600px] h-[560px] p-0 flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-5 border-b border-border-subtle shrink-0">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-[10px] bg-neon-purple/20 flex items-center justify-center">
              <Shield className="w-5 h-5 text-neon-purple" />
            </div>
            <div className="space-y-0.5">
              <h2 className="text-lg font-semibold text-text-primary">Role Permissions</h2>
              <p className="text-xs text-text-muted">Configure permissions for <span className="capitalize">{role.name}</span></p>
            </div>
          </div>
          <button type="button" onClick={onClose} className="w-8 h-8 rounded-lg bg-surface flex items-center justify-center text-text-secondary hover:text-text-primary">
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 px-6 py-4 overflow-hidden flex flex-col">
          {/* Search */}
          <SearchInput
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search permissions..."
            className="mb-4"
          />

          {/* Permission Groups */}
          <div className="flex-1 overflow-y-auto space-y-2">
            {filteredGroups.map(group => {
              // Only consider permissions allowed for Roles
              const allowedPerms = group.permissions
              const allGranted = allowedPerms.length > 0 && allowedPerms.every(p => permissions[p.name])
              const someGranted = allowedPerms.some(p => permissions[p.name])

              return (
                <div key={group.name} className="border border-border-subtle rounded-lg overflow-hidden">
                  {/* Group Header */}
                  <div className="flex items-center justify-between px-3 py-2 bg-bg-base/50 rounded-lg">
                    <div className="flex items-center gap-3">
                      <input
                        type="checkbox"
                        checked={allGranted}
                        ref={(el) => {
                          if (el) el.indeterminate = someGranted && !allGranted
                        }}
                        onChange={() => toggleGroup(group.name, !allGranted)}
                        className="h-4 w-4 rounded border-border-default bg-bg-base text-neon-cyan focus:ring-neon-cyan focus:ring-offset-0"
                      />
                      <span className="text-sm font-medium text-text-primary">{group.displayName}</span>
                    </div>
                  </div>

                  {/* Permissions */}
                  <div className="pl-9 pr-3 py-2 space-y-0.5">
                    {group.permissions.map(perm => (
                      <label
                        key={perm.name}
                        className="flex items-center gap-3 cursor-pointer hover:bg-surface-elevated px-2 py-1.5 rounded"
                      >
                        <input
                          type="checkbox"
                          checked={permissions[perm.name] || false}
                          onChange={() => togglePermission(perm.name)}
                          className="h-4 w-4 rounded border-border-default bg-bg-base text-neon-cyan focus:ring-neon-cyan focus:ring-offset-0"
                        />
                        <span className={cn(
                          "text-sm",
                          perm.parentName ? "text-text-muted" : "text-text-secondary"
                        )}>
                          {perm.displayName}
                        </span>
                      </label>
                    ))}
                  </div>
                </div>
              )
            })}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between px-6 py-4 border-t border-border-subtle shrink-0">
          <span className="text-xs font-mono text-text-muted">
            {grantedCount} permissions granted
          </span>
          <div className="flex gap-3">
            <Button type="button" variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button variant="purple" onClick={handleSave} disabled={isLoading} className="gap-1.5">
              <Save className="w-3.5 h-3.5" />
              {isLoading ? "Saving..." : "Save Permissions"}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
