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
  // Provider configuration from backend
  const [providerConfigs, setProviderConfigs] = useState<PermissionProvider[]>([])
  const [activeProviderType, setActiveProviderType] = useState<PermissionProviderName | null>(null)
  const [search, setSearch] = useState("")
  
  // Data for each provider type
  const [roles, setRoles] = useState<Role[]>([])
  const [users, setUsers] = useState<UserType[]>([])
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

  const loadInitialData = async () => {
    const [providers, rolesResult, usersResult] = await Promise.all([
      getPermissionProviders(),
      getRoles(),
      getUsers({ take: 100 }),
    ])
    
    setProviderConfigs(providers)
    setRoles(rolesResult)
    setUsers(usersResult.items)
    
    // Select first enabled provider as default tab
    if (providers.length > 0) {
      setActiveProviderType(providers[0].name)
    }
  }

  // ============================================================
  //  Reset selection when provider type (tab) changes
  // ============================================================
  useEffect(() => {
    if (!activeProviderType) return

    // Select first item for the current provider type
    if (activeProviderType === "R" && roles.length > 0) {
      setSelectedProviderKey(roles[0].name)
    } else if (activeProviderType === "U" && users.length > 0) {
      setSelectedProviderKey(users[0].id)
    } else if (activeProviderType === "C") {
      setSelectedProviderKey("frontend")
    } else {
      setSelectedProviderKey(null)
    }
    
    // Clear pending changes when switching provider type
    setPendingChanges(new Map())
  }, [activeProviderType, roles.length, users.length])

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
    
    setIsLoading(true)
    try {
      const groups = await getPermissions(activeProviderType, selectedProviderKey)
      setPermissionGroups(groups)
      setExpandedGroups(new Set(groups.map(g => g.name)))
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
  const filteredGroups = useMemo(() => {
    if (!activeProviderType) return []
    
    return permissionGroups
      .map(group => ({
        ...group,
        permissions: group.permissions.filter(p => {
          // Empty allowedProviders = allowed for all providers
          const isAllowed = p.allowedProviders.length === 0 || 
                           p.allowedProviders.includes(activeProviderType)
          const matchesSearch = search === "" ||
            p.displayName.toLowerCase().includes(search.toLowerCase()) ||
            group.displayName.toLowerCase().includes(search.toLowerCase())
          return isAllowed && matchesSearch
        })
      }))
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
      subtitle="Manage permissions for roles, users and clients"
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

      <div className="flex gap-6">
        {/* Left Panel - Provider Selection */}
        <div className="w-72 shrink-0">
          <div className="border border-border-subtle rounded-lg bg-surface overflow-hidden">
            <div className="flex items-center justify-between px-4 py-3 border-b border-border-subtle">
              <span className="text-sm font-semibold text-text-primary">
                {leftPanelHeader}
              </span>
              <Badge variant="purple" className="text-[10px] normal-case">
                {selectableProviders.length} items
              </Badge>
            </div>
            
            <div className="max-h-[500px] overflow-y-auto p-2 space-y-1">
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
            </div>
          </div>
        </div>

        {/* Right Panel - Permissions */}
        <div className="flex-1 min-w-0">
          {selectedProviderKey ? (
            <div className="border border-border-subtle rounded-lg bg-surface overflow-hidden">
              {/* Header */}
              <div className="flex items-center justify-between px-4 py-3 border-b border-border-subtle">
                <div className="flex items-center gap-3">
                  <span className="text-sm font-semibold text-text-primary capitalize">
                    {selectedProviderLabel} Permissions
                  </span>
                  <Badge variant="green" className="text-[10px] normal-case">
                    {filteredGroups.reduce((acc, g) => acc + g.permissions.filter(p => p.isGranted).length, 0)} granted
                  </Badge>
                  {isDirty && (
                    <Badge variant="gold" className="text-[10px] normal-case">
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
              <div className="px-4 py-3 border-b border-border-subtle">
                <SearchInput
                  placeholder="Search permissions..."
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  className="w-full"
                />
              </div>

              {/* Permission Groups */}
              <div className="max-h-[400px] overflow-y-auto">
                {filteredGroups.map((group) => {
                  const isExpanded = expandedGroups.has(group.name)
                  const grantedCount = group.permissions.filter(p => p.isGranted).length
                  const totalCount = group.permissions.length

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
                        <Badge variant={grantedCount === totalCount ? "green" : grantedCount > 0 ? "gold" : "default"}>
                          {grantedCount}/{totalCount}
                        </Badge>
                      </button>

                      {/* Permissions */}
                      {isExpanded && (
                        <div className="px-4 py-2 space-y-1">
                          {group.permissions.map((perm) => {
                            // Check if permission is inherited from other providers
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
                                  "flex items-center gap-3 px-3 py-2 rounded-md cursor-pointer",
                                  isInherited && !perm.isGranted
                                    ? "bg-neon-purple/5 hover:bg-neon-purple/10"
                                    : "hover:bg-surface-elevated"
                                )}
                                title={isInherited && !perm.isGranted
                                  ? `Inherited from role: ${inheritedRoles.join(', ')}`
                                  : undefined
                                }
                              >
                                <input
                                  type="checkbox"
                                  checked={perm.isGranted}
                                  onChange={() => togglePermission(perm.name, perm.isGranted)}
                                  className="h-4 w-4 rounded border-border-default bg-bg-base text-neon-cyan focus:ring-neon-cyan focus:ring-offset-0"
                                />
                                <span className={cn(
                                  "text-sm flex-1",
                                  perm.parentName ? "ml-4 text-text-muted" : "text-text-secondary"
                                )}>
                                  {perm.displayName}
                                </span>
                                {/* Show inherited badge for User Permissions */}
                                {activeProviderType === 'U' && isInherited && !perm.isGranted && (
                                  <span className="text-[10px] px-1.5 py-0.5 rounded bg-neon-purple/20 text-neon-purple border border-neon-purple/30">
                                    via {inheritedRoles.join(', ')}
                                  </span>
                                )}
                              </label>
                            )
                          })}
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            </div>
          ) : (
            <div className="flex items-center justify-center h-64 text-text-muted border border-border-subtle rounded-lg bg-surface">
              Select a {currentProviderConfig?.displayName.replace(" Permissions", "").toLowerCase() || "provider"} to manage permissions
            </div>
          )}
        </div>
      </div>
    </AdminLayout>
  )
}
