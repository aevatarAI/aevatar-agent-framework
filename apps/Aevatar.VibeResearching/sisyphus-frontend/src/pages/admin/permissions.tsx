import { useState, useEffect, useMemo } from "react"
import { Shield, User, Monitor, ChevronDown, ChevronRight, Check, X, Save } from "lucide-react"
import { AdminLayout } from "@/components/admin"
import { Button } from "@/components/ui/button"
import { SearchInput } from "@/components/ui/input"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Badge } from "@/components/ui/badge"
import { 
  getPermissions, 
  updatePermissions, 
  grantAllPermissions, 
  revokeAllPermissions,
  getPermissionProviders,
  getRoles,
  getUsers,
} from "@/lib/abp"
import type { 
  Role, 
  User as UserType, 
  PermissionGroup, 
  PermissionProvider,
  PermissionProviderName,
} from "@/types/user-management"
import { cn } from "@/lib/utils"
import { usePermission } from "@/hooks/use-permission"

// ============================================================
//  Icon mapping for provider types
// ============================================================
const PROVIDER_ICONS: Record<PermissionProvider["icon"], React.ReactNode> = {
  shield: <Shield className="w-4 h-4" />,
  user: <User className="w-4 h-4" />,
  monitor: <Monitor className="w-4 h-4" />,
}

// ============================================================
//  Permissions Admin Page (ABP Compliant - Dynamic Providers)
// ============================================================

export default function PermissionsPage() {
  const { hasPermission, isAdmin } = usePermission()

  // Provider configuration from backend
  const [providerConfigs, setProviderConfigs] = useState<PermissionProvider[]>([])
  const [activeProviderType, setActiveProviderType] = useState<PermissionProviderName | null>(null)
  const [search, setSearch] = useState("")
  
  // Data for each provider type
  const [roles, setRoles] = useState<Role[]>([])
  const [users, setUsers] = useState<UserType[]>([])
  const [userSearch, setUserSearch] = useState("")
  const [userTotalCount, setUserTotalCount] = useState(0)
  const [isLoadingMoreUsers, setIsLoadingMoreUsers] = useState(false)
  const [selectedProviderKey, setSelectedProviderKey] = useState<string | null>(null)
  const [permissionGroups, setPermissionGroups] = useState<PermissionGroup[]>([])
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(new Set())
  const [, setIsLoading] = useState(false)
  const [isSaving, setIsSaving] = useState(false)
  
  // Batch save: track pending changes
  const [pendingChanges, setPendingChanges] = useState<Map<string, boolean>>(new Map())
  const isDirty = pendingChanges.size > 0

  // ============================================================
  //  Load provider configuration on mount
  // ============================================================
  useEffect(() => {
    loadInitialData()
  }, [])

  // 按需加载 — 只请求用户有权限访问的数据，其余容错降级
  const canManageRolePerms = isAdmin || hasPermission("AbpIdentity.Roles.ManagePermissions")
  const canManageUserPerms = isAdmin || hasPermission("AbpIdentity.Users.ManagePermissions")

  const loadInitialData = async () => {
    const [providers, rolesResult, usersResult] = await Promise.all([
      getPermissionProviders(),
      canManageRolePerms
        ? getRoles().catch(() => [] as Role[])
        : Promise.resolve([] as Role[]),
      canManageUserPerms
        ? getUsers({ take: 50 }).catch(() => ({ items: [] as UserType[], totalCount: 0 }))
        : Promise.resolve({ items: [] as UserType[], totalCount: 0 }),
    ])
    
    // Filter tabs based on user permissions
    const visibleProviders = isAdmin
      ? providers
      : providers.filter(p => {
          if (p.name === "R") return canManageRolePerms
          if (p.name === "U") return canManageUserPerms
          return true
        })

    setProviderConfigs(visibleProviders)
    setRoles(rolesResult)
    setUsers(usersResult.items)
    setUserTotalCount(usersResult.totalCount)
    
    // Select first visible provider as default tab
    if (visibleProviders.length > 0) {
      setActiveProviderType(visibleProviders[0].name)
    }
  }

  // ============================================================
  //  User search with debounce
  // ============================================================
  useEffect(() => {
    const timer = setTimeout(() => {
      searchUsers(userSearch)
    }, 300)
    return () => clearTimeout(timer)
  }, [userSearch])

  const searchUsers = async (query: string) => {
    try {
      const result = await getUsers({ take: 50, search: query || undefined })
      setUsers(result.items)
      setUserTotalCount(result.totalCount)
      if (activeProviderType === "U") {
        setSelectedProviderKey(result.items.length > 0 ? result.items[0].id : null)
        if (result.items.length === 0) setPermissionGroups([])
      }
    } catch {
      // 权限不足或网络错误时静默降级
    }
  }

  const loadMoreUsers = async () => {
    if (isLoadingMoreUsers || users.length >= userTotalCount) return
    setIsLoadingMoreUsers(true)
    try {
      const result = await getUsers({
        skip: users.length,
        take: 50,
        search: userSearch || undefined,
      })
      setUsers(prev => [...prev, ...result.items])
      setUserTotalCount(result.totalCount)
    } catch {
      // 权限不足或网络错误时静默降级
    } finally {
      setIsLoadingMoreUsers(false)
    }
  }

  // ============================================================
  //  Reset selection when provider type (tab) changes
  // ============================================================
  useEffect(() => {
    if (!activeProviderType) return

    // Full reset: clear stale data from the previous tab
    setSelectedProviderKey(null)
    setPermissionGroups([])
    setPendingChanges(new Map())
  }, [activeProviderType])

  // Auto-select first item when nothing is selected (after tab switch or initial load)
  useEffect(() => {
    if (selectedProviderKey) return
    if (!activeProviderType) return

    if (activeProviderType === "R" && roles.length > 0) {
      setSelectedProviderKey(roles[0].name)
    } else if (activeProviderType === "U" && users.length > 0) {
      setSelectedProviderKey(users[0].id)
    } else if (activeProviderType === "C") {
      setSelectedProviderKey("frontend")
    }
  }, [activeProviderType, roles.length, users.length, selectedProviderKey])

  // ============================================================
  //  Load permissions when selected provider changes
  // ============================================================
  useEffect(() => {
    if (selectedProviderKey && activeProviderType) {
      loadPermissions()
      setPendingChanges(new Map())
    }
  }, [selectedProviderKey, activeProviderType])

  const loadPermissions = async () => {
    if (!selectedProviderKey || !activeProviderType) return

    // Validate providerKey matches the current provider type to prevent mismatched requests
    if (activeProviderType === "U") {
      // User providerKey should be a GUID (contains hyphens), not a role name
      if (!selectedProviderKey.includes("-")) return
    }
    
    setIsLoading(true)
    try {
      const groups = await getPermissions(activeProviderType, selectedProviderKey)

      // Normalize hierarchy: if any child is granted, ensure its parent chain is also granted.
      // This fixes data inconsistencies from legacy permission assignments.
      const normalized = groups.map(group => {
        const permsByName = new Map(group.permissions.map(p => [p.name, p]))
        let changed = true
        while (changed) {
          changed = false
          for (const perm of group.permissions) {
            if (perm.isGranted && perm.parentName) {
              const parent = permsByName.get(perm.parentName)
              if (parent && !parent.isGranted) {
                parent.isGranted = true
                changed = true
              }
            }
          }
        }
        return group
      })

      setPermissionGroups(normalized)
      setExpandedGroups(new Set(normalized.map(g => g.name)))
    } finally {
      setIsLoading(false)
    }
  }

  // ============================================================
  //  Permission hierarchy helpers
  // ============================================================
  const allPermissions = useMemo(() => 
    permissionGroups.flatMap(g => g.permissions), 
    [permissionGroups]
  )

  // Build parent -> children map
  const childrenMap = useMemo(() => {
    const map = new Map<string, string[]>()
    allPermissions.forEach(perm => {
      if (perm.parentName) {
        const children = map.get(perm.parentName) || []
        children.push(perm.name)
        map.set(perm.parentName, children)
      }
    })
    return map
  }, [allPermissions])

  // Get all descendants recursively
  const getDescendants = (name: string): string[] => {
    const children = childrenMap.get(name) || []
    return children.flatMap(child => [child, ...getDescendants(child)])
  }

  // Find parent permission
  const findParent = (name: string): string | undefined => {
    return allPermissions.find(p => p.name === name)?.parentName
  }

  // Count granted permissions - parent permissions count as real permissions (e.g. Read/View access)
  const countGranted = (permissions: typeof allPermissions): number => {
    return permissions.filter(p => p.isGranted).length
  }

  // ============================================================
  //  Permission operations
  // ============================================================
  const toggleGroup = (groupName: string) => {
    setExpandedGroups(prev => {
      const next = new Set(prev)
      if (next.has(groupName)) {
        next.delete(groupName)
      } else {
        next.add(groupName)
      }
      return next
    })
  }

  const togglePermission = (permName: string, currentValue: boolean) => {
    if (!selectedProviderKey) return

    const newValue = !currentValue
    const updates: Record<string, boolean> = { [permName]: newValue }

    if (newValue) {
      // Granting: auto-grant all ancestors (parent chain)
      let parent = findParent(permName)
      while (parent) {
        updates[parent] = true
        parent = findParent(parent)
      }
    } else {
      // Revoking: auto-revoke all descendants
      const descendants = getDescendants(permName)
      descendants.forEach(d => {
        updates[d] = false
      })
    }

    // Update UI optimistically
    setPermissionGroups(prev =>
      prev.map(group => ({
        ...group,
        permissions: group.permissions.map(p =>
          updates[p.name] !== undefined ? { ...p, isGranted: updates[p.name] } : p
        ),
      }))
    )

    // Track pending changes for batch save
    setPendingChanges(prev => {
      const next = new Map(prev)
      Object.entries(updates).forEach(([name, granted]) => {
        next.set(name, granted)
      })
      return next
    })
  }

  const handleSave = async () => {
    if (!selectedProviderKey || !activeProviderType || pendingChanges.size === 0) return
    
    setIsSaving(true)
    try {
      const updates = Array.from(pendingChanges.entries()).map(([name, isGranted]) => ({
        name,
        isGranted,
      }))
      await updatePermissions(activeProviderType, selectedProviderKey, updates)
      setPendingChanges(new Map())
    } finally {
      setIsSaving(false)
    }
  }

  const handleGrantAll = async () => {
    if (!selectedProviderKey || !activeProviderType) return
    setIsSaving(true)
    try {
      await grantAllPermissions(activeProviderType, selectedProviderKey)
      await loadPermissions()
      setPendingChanges(new Map())
    } finally {
      setIsSaving(false)
    }
  }

  const handleRevokeAll = async () => {
    if (!selectedProviderKey || !activeProviderType) return
    setIsSaving(true)
    try {
      await revokeAllPermissions(activeProviderType, selectedProviderKey)
      await loadPermissions()
      setPendingChanges(new Map())
    } finally {
      setIsSaving(false)
    }
  }

  // ============================================================
  //  Computed values
  // ============================================================
  const currentProviderConfig = providerConfigs.find(p => p.name === activeProviderType)

  // Filter groups by search AND allowedProviders
  // NOTE: Empty allowedProviders array means permission is allowed for ALL provider types
  // When a child permission matches search, its parent chain is auto-included
  // to preserve the tree structure for rendering.
  const filteredGroups = useMemo(() => {
    if (!activeProviderType) return []
    const q = search.toLowerCase()

    return permissionGroups
      .map(group => {
        // Step 1: filter by allowedProviders
        const allowed = group.permissions.filter(p =>
          p.allowedProviders.length === 0 || p.allowedProviders.includes(activeProviderType)
        )
        if (q === "") return { ...group, permissions: allowed }

        // Step 2: find permissions that directly match the search
        const matchSet = new Set(
          allowed
            .filter(p =>
              p.displayName.toLowerCase().includes(q) ||
              group.displayName.toLowerCase().includes(q)
            )
            .map(p => p.name)
        )

        // Step 3: for every match, walk up the parent chain and include ancestors
        const permByName = new Map(allowed.map(p => [p.name, p]))
        for (const name of [...matchSet]) {
          let cur = permByName.get(name)
          while (cur?.parentName) {
            matchSet.add(cur.parentName)
            cur = permByName.get(cur.parentName)
          }
        }

        return { ...group, permissions: allowed.filter(p => matchSet.has(p.name)) }
      })
      .filter(group => group.permissions.length > 0)
  }, [permissionGroups, activeProviderType, search])

  // Get selectable providers for current tab
  const selectableProviders = useMemo(() => {
    if (!activeProviderType || !currentProviderConfig) return []
    
    const icon = PROVIDER_ICONS[currentProviderConfig.icon]
    
    if (activeProviderType === "R") {
      return roles.map(r => ({ id: r.name, label: r.name, icon }))
    } else if (activeProviderType === "U") {
      // Use userName as fallback when name/surname are empty
      return users.map(u => {
        const fullName = [u.name, u.surname].filter(Boolean).join(' ').trim()
        return { id: u.id, label: fullName || u.userName || u.email || u.id, icon }
      })
    } else if (activeProviderType === "C") {
      return [{ id: "frontend", label: "Frontend App", icon }]
    }
    return []
  }, [activeProviderType, currentProviderConfig, roles, users])

  // Left panel header text
  const leftPanelHeader = useMemo(() => {
    if (activeProviderType === "R") return "Select Role"
    if (activeProviderType === "U") return "Select User"
    if (activeProviderType === "C") return "Select Client"
    return "Select Provider"
  }, [activeProviderType])

  // Selected provider display label (for header)
  const selectedProviderLabel = useMemo(() => {
    if (!selectedProviderKey) return ""
    const provider = selectableProviders.find(p => p.id === selectedProviderKey)
    return provider?.label || selectedProviderKey
  }, [selectedProviderKey, selectableProviders])

  // ============================================================
  //  Render
  // ============================================================
  
  // Show loading if providers not loaded yet
  if (providerConfigs.length === 0) {
    return (
      <AdminLayout title="Permissions" subtitle="Loading permission providers...">
        <div className="flex items-center justify-center h-64 text-text-muted">
          Loading...
        </div>
      </AdminLayout>
    )
  }

  return (
    <AdminLayout
      title="Permissions"
      subtitle={`Manage permissions for ${providerConfigs.map(p => p.displayName.replace(' Permissions', '').toLowerCase()).join(', ')}`}
    >
      {/* Dynamic Tabs from Provider Config */}
      <Tabs 
        value={activeProviderType || ""} 
        onValueChange={(v) => setActiveProviderType(v as PermissionProviderName)} 
        className="mb-6"
      >
        <TabsList>
          {providerConfigs.map((provider) => (
            <TabsTrigger key={provider.name} value={provider.name} className="gap-1.5">
              {PROVIDER_ICONS[provider.icon]}
              {provider.displayName}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      <div className="flex gap-6 h-[calc(100vh-220px)] min-h-[400px]">
        {/* Left Panel - Provider Selection */}
        <div className="w-72 shrink-0 flex flex-col">
          <div className="border border-border-subtle rounded-lg bg-surface overflow-hidden flex flex-col h-full">
            <div className="flex items-center justify-between px-4 py-3 border-b border-border-subtle shrink-0">
              <span className="text-sm font-semibold text-text-primary">
                {leftPanelHeader}
              </span>
              <Badge variant="purple" className="text-[10px] normal-case">
                {activeProviderType === "U" ? `${userTotalCount} items` : `${selectableProviders.length} items`}
              </Badge>
            </div>

            {/* User search box - only for User Permissions tab */}
            {activeProviderType === "U" && (
              <div className="px-3 py-2 border-b border-border-subtle shrink-0">
                <SearchInput
                  placeholder="Search users..."
                  value={userSearch}
                  onChange={(e) => setUserSearch(e.target.value)}
                  className="w-full"
                />
              </div>
            )}
            
            <div className="flex-1 overflow-y-auto p-2 space-y-1">
              {selectableProviders.map((provider) => (
                <button
                  key={provider.id}
                  onClick={() => setSelectedProviderKey(provider.id)}
                  className={cn(
                    "w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-left transition-colors",
                    selectedProviderKey === provider.id
                      ? "bg-neon-purple/15 text-neon-purple border border-neon-purple/40"
                      : "text-text-secondary hover:bg-surface-elevated border border-transparent"
                  )}
                >
                  <span className={cn(
                    "w-8 h-8 rounded-lg flex items-center justify-center shrink-0",
                    selectedProviderKey === provider.id
                      ? "bg-neon-purple/20"
                      : "bg-surface-elevated"
                  )}>
                    {provider.icon}
                  </span>
                  <span className="text-sm font-medium truncate capitalize">{provider.label}</span>
                </button>
              ))}

              {/* Load more button for users */}
              {activeProviderType === "U" && users.length < userTotalCount && (
                <button
                  onClick={loadMoreUsers}
                  disabled={isLoadingMoreUsers}
                  className="w-full py-2 text-xs text-neon-cyan hover:text-neon-cyan/80 transition-colors disabled:opacity-50"
                >
                  {isLoadingMoreUsers ? "Loading..." : `Load more (${users.length}/${userTotalCount})`}
                </button>
              )}
            </div>
          </div>
        </div>

        {/* Right Panel - Permissions */}
        <div className="flex-1 min-w-0 flex flex-col">
          {selectedProviderKey ? (
            <div className="border border-border-subtle rounded-lg bg-surface overflow-hidden flex flex-col h-full">
              {/* Header */}
              <div className="flex items-center justify-between px-4 py-3 border-b border-border-subtle shrink-0">
                <div className="flex items-center gap-3 min-w-0">
                  <span className="text-sm font-semibold text-text-primary truncate max-w-[200px]" title={selectedProviderLabel}>
                    {selectedProviderLabel}
                  </span>
                  <Badge variant="purple" className="text-[10px] normal-case shrink-0">
                    {filteredGroups.reduce((acc, g) => acc + g.permissions.length, 0)} permissions
                  </Badge>
                  <Badge variant="green" className="text-[10px] normal-case shrink-0">
                    {filteredGroups.reduce((acc, g) => acc + countGranted(g.permissions), 0)} granted
                  </Badge>
                  {isDirty && (
                    <Badge variant="gold" className="text-[10px] normal-case shrink-0">
                      {pendingChanges.size} unsaved
                    </Badge>
                  )}
                </div>
                <div className="flex items-center gap-3">
                  <div className="flex items-center gap-1.5">
                    <Button
                      variant="successOutline"
                      size="sm"
                      onClick={handleGrantAll}
                      disabled={isSaving}
                      className="gap-1"
                    >
                      <Check className="w-3.5 h-3.5" />
                      Grant All
                    </Button>
                    <Button
                      variant="destructiveOutline"
                      size="sm"
                      onClick={handleRevokeAll}
                      disabled={isSaving}
                      className="gap-1"
                    >
                      <X className="w-3.5 h-3.5" />
                      Revoke All
                    </Button>
                  </div>
                  <button
                    onClick={() => {
                      const allGroupNames = permissionGroups.map(g => g.name)
                      setExpandedGroups(prev =>
                        prev.size === allGroupNames.length ? new Set() : new Set(allGroupNames)
                      )
                    }}
                    className="text-xs text-neon-cyan hover:underline"
                  >
                    {expandedGroups.size === permissionGroups.length ? "Collapse All" : "Expand All"}
                  </button>
                  <Button 
                    variant="purple" 
                    size="sm" 
                    disabled={isSaving || !isDirty}
                    onClick={handleSave}
                    className="gap-1"
                  >
                    <Save className="w-3.5 h-3.5" />
                    {isSaving ? "Saving..." : "Save"}
                  </Button>
                </div>
              </div>

              {/* Search */}
              <div className="px-4 py-3 border-b border-border-subtle shrink-0">
                <SearchInput
                  placeholder="Search permissions..."
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  className="w-full"
                />
              </div>

              {/* Permission Groups */}
              <div className="flex-1 overflow-y-auto">
                {filteredGroups.map((group) => {
                  const isExpanded = expandedGroups.has(group.name)
                  const granted = countGranted(group.permissions)
                  const rawTotal = group.permissions.length
                  const allGranted = granted === rawTotal && rawTotal > 0

                  return (
                    <div key={group.name} className="border-b border-border-subtle last:border-0">
                      {/* Group Header */}
                      <button
                        onClick={() => toggleGroup(group.name)}
                        className="w-full flex items-center justify-between px-4 py-3 bg-surface-elevated/50 hover:bg-surface-elevated transition-colors"
                      >
                        <div className="flex items-center gap-3">
                          {isExpanded ? (
                            <ChevronDown className="w-4 h-4 text-text-muted" />
                          ) : (
                            <ChevronRight className="w-4 h-4 text-text-muted" />
                          )}
                          <span className="font-medium text-text-primary">{group.displayName}</span>
                        </div>
                        <Badge variant={allGranted ? "green" : granted > 0 ? "gold" : "default"}>
                          {granted}/{rawTotal}
                        </Badge>
                      </button>

                      {/* Permissions - Tree Structure */}
                      {isExpanded && (
                        <div className="px-4 py-2 space-y-0.5">
                          {(() => {
                            // Build tree: group permissions by parent
                            const parentPerms = group.permissions.filter(p => !p.parentName)
                            const childPerms = group.permissions.filter(p => !!p.parentName)
                            const childrenByParent = new Map<string, typeof group.permissions>()
                            childPerms.forEach(p => {
                              const list = childrenByParent.get(p.parentName!) || []
                              list.push(p)
                              childrenByParent.set(p.parentName!, list)
                            })

                            // Standalone permissions (no parent, no children)
                            const standalonePerms = parentPerms.filter(p => !childrenByParent.has(p.name))
                            // Parent permissions (has children)
                            const treeParents = parentPerms.filter(p => childrenByParent.has(p.name))

                            const renderPermCheckbox = (perm: typeof group.permissions[0], isChild: boolean, isLast?: boolean) => {
                              const inheritedFrom = perm.grantedProviders?.filter(
                                gp => gp.providerName !== activeProviderType
                              ) || []
                              const isInherited = inheritedFrom.length > 0
                              const inheritedRoles = inheritedFrom
                                .filter(gp => gp.providerName === 'R')
                                .map(gp => gp.providerKey)

                              return (
                                <label
                                  key={perm.name}
                                  className={cn(
                                    "flex items-center gap-3 py-1.5 rounded-md cursor-pointer",
                                    isChild ? "pl-4 pr-3" : "px-3",
                                    isInherited && !perm.isGranted
                                      ? "bg-neon-purple/5 hover:bg-neon-purple/10"
                                      : "hover:bg-surface-elevated"
                                  )}
                                  title={isInherited && !perm.isGranted
                                    ? `Inherited from role: ${inheritedRoles.join(', ')}`
                                    : undefined
                                  }
                                >
                                  {/* Tree connector for children */}
                                  {isChild && (
                                    <span className="text-border-default text-xs w-4 text-center select-none shrink-0">
                                      {isLast ? "└" : "├"}
                                    </span>
                                  )}
                                  <input
                                    type="checkbox"
                                    checked={perm.isGranted}
                                    onChange={() => togglePermission(perm.name, perm.isGranted)}
                                    className="h-4 w-4 rounded border-border-default bg-bg-base text-neon-cyan focus:ring-neon-cyan focus:ring-offset-0 shrink-0"
                                  />
                                  <span className={cn(
                                    "text-sm flex-1",
                                    isChild ? "text-text-muted" : "font-medium text-text-secondary"
                                  )}>
                                    {perm.displayName}
                                  </span>
                                  {activeProviderType === 'U' && isInherited && !perm.isGranted && (
                                    <span className="text-[10px] px-1.5 py-0.5 rounded bg-neon-purple/20 text-neon-purple border border-neon-purple/30 shrink-0">
                                      via {inheritedRoles.join(', ')}
                                    </span>
                                  )}
                                </label>
                              )
                            }

                            return (
                              <>
                                {/* Parent permissions with their children */}
                                {treeParents.map((parent) => {
                                  const children = childrenByParent.get(parent.name) || []
                                  return (
                                    <div key={parent.name} className="mb-1">
                                      {/* Parent row */}
                                      {renderPermCheckbox(parent, false)}
                                      {/* Children with left border */}
                                      {children.length > 0 && (
                                        <div className="ml-6 border-l border-border-subtle">
                                          {children.map((child, idx) =>
                                            renderPermCheckbox(child, true, idx === children.length - 1)
                                          )}
                                        </div>
                                      )}
                                    </div>
                                  )
                                })}
                                {/* Standalone permissions (no children) */}
                                {standalonePerms.map((perm) => renderPermCheckbox(perm, false))}
                              </>
                            )
                          })()}
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            </div>
          ) : (
            <div className="flex items-center justify-center h-full text-text-muted border border-border-subtle rounded-lg bg-surface">
              Select a {currentProviderConfig?.displayName.replace(" Permissions", "").toLowerCase() || "provider"} to manage permissions
            </div>
          )}
        </div>
      </div>
    </AdminLayout>
  )
}
