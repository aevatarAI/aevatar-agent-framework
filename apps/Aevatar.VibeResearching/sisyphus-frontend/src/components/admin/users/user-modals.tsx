import React, { useState, useEffect } from "react"
import { X, Trash2, User, ShieldCheck, Shield, Users, Pencil, UserPlus } from "lucide-react"
import { Dialog, DialogContent } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input, PasswordInput } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Avatar } from "@/components/ui/avatar"
import { useToast } from "@/components/ui/toast"
import type { User as UserType, CreateUserInput, UpdateUserInput } from "@/types/user-management"
import { cn } from "@/lib/utils"

// ============================================================
//  Create User Modal
// ============================================================

interface CreateUserModalProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: CreateUserInput) => Promise<void>
  roles: string[]
}

export const CreateUserModal: React.FC<CreateUserModalProps> = ({
  open,
  onClose,
  onSubmit,
  roles,
}) => {
  const [isLoading, setIsLoading] = useState(false)
  const { error: showError } = useToast()
  const [formData, setFormData] = useState<CreateUserInput>({
    userName: "",
    email: "",
    name: "",
    surname: "",
    phoneNumber: "",
    password: "",
    isActive: true,
    lockoutEnabled: false,
    roleNames: ["member"],  // Default role for new users
  })

  // ABP Password Policy: min 6 chars + uppercase + lowercase + digit + special char
  const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]).{6,}$/

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    // ABP Password Policy Validation (same as register page)
    if (!passwordRegex.test(formData.password)) {
      showError("Password must be at least 6 characters with uppercase, lowercase, number & special character")
      return
    }

    setIsLoading(true)
    try {
      await onSubmit(formData)
      onClose()
    } finally {
      setIsLoading(false)
    }
  }

  const toggleRole = (role: string) => {
    setFormData(prev => ({
      ...prev,
      roleNames: prev.roleNames.includes(role)
        ? prev.roleNames.filter(r => r !== role)
        : [...prev.roleNames, role],
    }))
  }

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[520px]">
        <form onSubmit={handleSubmit}>
          {/* Header */}
          <div className="flex items-center justify-between p-5 border-b border-border-subtle">
            <h2 className="text-lg font-semibold text-text-primary">Create New User</h2>
            <button type="button" onClick={onClose} className="text-text-muted hover:text-text-primary">
              <X className="w-5 h-5" />
            </button>
          </div>

          {/* Body */}
          <div className="p-5 space-y-4 max-h-[60vh] overflow-y-auto">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <label className="text-sm text-text-secondary">First Name</label>
                <Input
                  value={formData.name}
                  onChange={(e) => setFormData(prev => ({ ...prev, name: e.target.value }))}
                  placeholder="John"
                />
              </div>
              <div className="space-y-1.5">
                <label className="text-sm text-text-secondary">Last Name</label>
                <Input
                  value={formData.surname}
                  onChange={(e) => setFormData(prev => ({ ...prev, surname: e.target.value }))}
                  placeholder="Doe"
                />
              </div>
            </div>

            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">Email *</label>
              <Input
                type="email"
                value={formData.email}
                onChange={(e) => setFormData(prev => ({ ...prev, email: e.target.value }))}
                placeholder="john@example.com"
                required
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">Username *</label>
              <Input
                value={formData.userName}
                onChange={(e) => setFormData(prev => ({ ...prev, userName: e.target.value }))}
                placeholder="johndoe"
                required
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">Password *</label>
              <PasswordInput
                value={formData.password}
                onChange={(e) => setFormData(prev => ({ ...prev, password: e.target.value }))}
                placeholder="Enter password"
                required
              />
              <p className="text-xs text-text-dimmed">
                Min 6 characters with uppercase, lowercase, number & special character
              </p>
            </div>

            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">Phone Number</label>
              <Input
                value={formData.phoneNumber}
                onChange={(e) => setFormData(prev => ({ ...prev, phoneNumber: e.target.value }))}
                placeholder="+1 (555) 000-0000"
              />
            </div>

            <div className="space-y-2">
              <label className="text-sm text-text-secondary">Assign Roles</label>
              <div className="flex flex-wrap gap-2">
                {roles.map(role => (
                  <button
                    key={role}
                    type="button"
                    onClick={() => toggleRole(role)}
                    className={cn(
                      "px-3 py-1.5 rounded-md text-sm border transition-all",
                      formData.roleNames.includes(role)
                        ? "bg-neon-cyan/15 border-neon-cyan/40 text-neon-cyan"
                        : "bg-surface border-border-subtle text-text-muted hover:border-border-default"
                    )}
                  >
                    {role}
                  </button>
                ))}
              </div>
            </div>

            <div className="flex items-center justify-between py-2">
              <div>
                <p className="text-sm text-text-primary">Active</p>
                <p className="text-xs text-text-muted">User can sign in</p>
              </div>
              <Switch
                checked={formData.isActive}
                onCheckedChange={(checked) => setFormData(prev => ({ ...prev, isActive: checked }))}
              />
            </div>
          </div>

          {/* Footer - Gold button per design */}
          <div className="flex justify-end gap-3 p-5 border-t border-border-subtle">
            <Button type="button" variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" variant="gold" disabled={isLoading} className="gap-1.5">
              <UserPlus className="w-4 h-4" />
              {isLoading ? "Creating..." : "Create User"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Edit User Modal
// ============================================================

interface EditUserModalProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: UpdateUserInput) => Promise<void>
  user: UserType | null
  roles: string[]
}

export const EditUserModal: React.FC<EditUserModalProps> = ({
  open,
  onClose,
  onSubmit,
  user,
  roles,
}) => {
  const [isLoading, setIsLoading] = useState(false)
  const [formData, setFormData] = useState<UpdateUserInput>({
    userName: "",
    email: "",
    name: "",
    surname: "",
    phoneNumber: "",
    isActive: true,
    lockoutEnabled: false,
    roleNames: [],
  })

  useEffect(() => {
    if (user) {
      setFormData({
        userName: user.userName,
        email: user.email,
        name: user.name || "",
        surname: user.surname || "",
        phoneNumber: user.phoneNumber || "",
        isActive: user.isActive,
        lockoutEnabled: user.lockoutEnabled,
        roleNames: user.roles,
      })
    }
  }, [user])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)
    try {
      await onSubmit(formData)
      onClose()
    } finally {
      setIsLoading(false)
    }
  }

  const toggleRole = (role: string) => {
    setFormData(prev => ({
      ...prev,
      roleNames: prev.roleNames.includes(role)
        ? prev.roleNames.filter(r => r !== role)
        : [...prev.roleNames, role],
    }))
  }

  if (!user) return null

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[520px]">
        <form onSubmit={handleSubmit}>
          {/* Header */}
          <div className="flex items-center justify-between p-5 border-b border-border-subtle">
            <div className="flex items-center gap-3 w-0 flex-1 mr-3">
              <Avatar name={[user.name, user.surname].filter(Boolean).join(' ') || user.userName || user.email.split('@')[0]} src={user.avatarUrl} size="md" className="flex-shrink-0" />
              <div className="w-0 flex-1">
                <h2 className="text-lg font-semibold text-text-primary">Edit User</h2>
                <p className="text-xs text-text-muted truncate" title={user.email}>{user.email}</p>
              </div>
            </div>
            <button type="button" onClick={onClose} className="text-text-muted hover:text-text-primary flex-shrink-0">
              <X className="w-5 h-5" />
            </button>
          </div>

          {/* Body */}
          <div className="p-5 space-y-4 max-h-[60vh] overflow-y-auto">
            {/* Read-only fields */}
            <div className="space-y-1">
              <label className="text-sm text-text-secondary">User Name</label>
              <Input
                value={user.userName}
                disabled
                className="bg-surface-elevated text-text-muted cursor-not-allowed"
              />
              <p className="text-xs text-text-dimmed">User name cannot be changed</p>
            </div>

            <div className="space-y-1">
              <label className="text-sm text-text-secondary">Email</label>
              <Input
                type="email"
                value={user.email}
                disabled
                className="bg-surface-elevated text-text-muted cursor-not-allowed"
              />
              <p className="text-xs text-text-dimmed">Email cannot be changed</p>
            </div>

            {/* Editable fields */}
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <label className="text-sm text-text-secondary">First Name</label>
                <Input
                  value={formData.name}
                  onChange={(e) => setFormData(prev => ({ ...prev, name: e.target.value }))}
                />
              </div>
              <div className="space-y-1.5">
                <label className="text-sm text-text-secondary">Last Name</label>
                <Input
                  value={formData.surname}
                  onChange={(e) => setFormData(prev => ({ ...prev, surname: e.target.value }))}
                />
              </div>
            </div>

            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">Phone Number</label>
              <Input
                value={formData.phoneNumber}
                onChange={(e) => setFormData(prev => ({ ...prev, phoneNumber: e.target.value }))}
              />
            </div>

            <div className="space-y-2">
              <label className="text-sm text-text-secondary">Assigned Roles</label>
              <div className="flex flex-wrap gap-2">
                {roles.map(role => (
                  <button
                    key={role}
                    type="button"
                    onClick={() => toggleRole(role)}
                    className={cn(
                      "px-3 py-1.5 rounded-md text-sm border transition-all",
                      formData.roleNames.includes(role)
                        ? "bg-neon-cyan/15 border-neon-cyan/40 text-neon-cyan"
                        : "bg-surface border-border-subtle text-text-muted hover:border-border-default"
                    )}
                  >
                    {role}
                  </button>
                ))}
              </div>
            </div>

            <div className="flex items-center justify-between py-2">
              <div>
                <p className="text-sm text-text-primary">Active</p>
                <p className="text-xs text-text-muted">User can sign in</p>
              </div>
              <Switch
                checked={formData.isActive}
                onCheckedChange={(checked) => setFormData(prev => ({ ...prev, isActive: checked }))}
              />
            </div>
          </div>

          {/* Footer */}
          <div className="flex justify-end gap-3 p-5 border-t border-border-subtle">
            <Button type="button" variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={isLoading}>
              {isLoading ? "Saving..." : "Save Changes"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Delete User Modal
// ============================================================

interface DeleteUserModalProps {
  open: boolean
  onClose: () => void
  onConfirm: () => Promise<void>
  user: UserType | null
}

export const DeleteUserModal: React.FC<DeleteUserModalProps> = ({
  open,
  onClose,
  onConfirm,
  user,
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

  if (!user) return null

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[420px] max-w-[90vw]">
        <div className="p-6 text-center overflow-hidden">
          <div className="mx-auto w-14 h-14 rounded-full bg-neon-red/10 border border-neon-red/30 flex items-center justify-center mb-4">
            <Trash2 className="w-7 h-7 text-neon-red" />
          </div>
          
          <h2 className="text-lg font-semibold text-text-primary mb-2">Delete User</h2>
          <p className="text-sm text-text-muted mb-1">
            Are you sure you want to delete this user?
          </p>
          <p className="text-sm font-mono text-neon-cyan mb-4 truncate max-w-full" title={user.email}>{user.email}</p>
          <p className="text-xs text-text-dimmed">
            This action cannot be undone. All user data will be permanently removed.
          </p>
        </div>

        <div className="flex gap-3 p-4 border-t border-border-subtle">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Cancel
          </Button>
          <Button
            variant="destructive"
            className="flex-1"
            onClick={handleConfirm}
            disabled={isLoading}
          >
            {isLoading ? "Deleting..." : "Delete User"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Set Password Modal
// ============================================================

interface SetPasswordModalProps {
  open: boolean
  onClose: () => void
  onSubmit: (password: string) => Promise<void>
  user: UserType | null
}

export const SetPasswordModal: React.FC<SetPasswordModalProps> = ({
  open,
  onClose,
  onSubmit,
  user,
}) => {
  const [isLoading, setIsLoading] = useState(false)
  const [password, setPassword] = useState("")
  const [confirmPassword, setConfirmPassword] = useState("")
  const { error: showError } = useToast()

  // ABP Password Policy: min 6 chars + uppercase + lowercase + digit + special char
  const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]).{6,}$/

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    // ABP Password Policy Validation (same as register page)
    if (!passwordRegex.test(password)) {
      showError("Password must be at least 6 characters with uppercase, lowercase, number & special character")
      return
    }

    if (password !== confirmPassword) {
      showError("Passwords do not match")
      return
    }

    setIsLoading(true)
    try {
      await onSubmit(password)
      onClose()
    } finally {
      setIsLoading(false)
    }
  }

  if (!user) return null

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[440px]">
        <form onSubmit={handleSubmit}>
          {/* Header */}
          <div className="flex items-center justify-between p-5 border-b border-border-subtle">
            <h2 className="text-lg font-semibold text-text-primary">Set Password</h2>
            <button type="button" onClick={onClose} className="text-text-muted hover:text-text-primary">
              <X className="w-5 h-5" />
            </button>
          </div>

          {/* User Info */}
          <div className="p-4 mx-5 mt-5 rounded-lg bg-surface-elevated border border-border-subtle">
            <div className="flex items-center gap-3">
              <Avatar name={[user.name, user.surname].filter(Boolean).join(' ') || user.userName || user.email.split('@')[0]} src={user.avatarUrl} size="md" className="flex-shrink-0" />
              <div className="w-0 flex-1">
                <p className="text-sm font-medium text-text-primary truncate" title={[user.name, user.surname].filter(Boolean).join(' ') || user.userName || user.email.split('@')[0]}>{[user.name, user.surname].filter(Boolean).join(' ') || user.userName || user.email.split('@')[0]}</p>
                <p className="text-xs text-text-muted truncate" title={user.email}>{user.email}</p>
              </div>
            </div>
          </div>

          {/* Body */}
          <div className="p-5 space-y-4">
            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">New Password</label>
              <PasswordInput
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter new password"
                required
              />
              <p className="text-xs text-text-dimmed">
                Min 6 characters with uppercase, lowercase, number & special character
              </p>
            </div>

            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">Confirm Password</label>
              <PasswordInput
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                placeholder="Confirm new password"
                required
              />
            </div>
          </div>

          {/* Footer */}
          <div className="flex justify-end gap-3 p-5 border-t border-border-subtle">
            <Button type="button" variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" variant="gold" disabled={isLoading}>
              {isLoading ? "Setting..." : "Set Password"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  User Detail Modal
// ============================================================

interface UserDetailModalProps {
  open: boolean
  onClose: () => void
  onEdit: () => void
  user: UserType | null
}

export const UserDetailModal: React.FC<UserDetailModalProps> = ({
  open,
  onClose,
  onEdit,
  user,
}) => {
  if (!user) return null

  // Build display name with fallbacks
  const fullName = [user.name, user.surname].filter(Boolean).join(' ').trim()
  const displayName = fullName || user.userName || user.email.split('@')[0]

  const formatDate = (dateString: string | undefined) => {
    if (!dateString) return "—"
    const date = new Date(dateString)
    return date.toLocaleDateString("en-US", {
      month: "short",
      day: "numeric",
      year: "numeric",
    })
  }

  const formatRelativeTime = (dateString: string | undefined) => {
    if (!dateString) return "—"
    const date = new Date(dateString)
    const now = new Date()
    const diffMs = now.getTime() - date.getTime()
    const diffHours = Math.floor(diffMs / (1000 * 60 * 60))
    const diffDays = Math.floor(diffHours / 24)

    if (diffHours < 1) return "Just now"
    if (diffHours < 24) return `${diffHours} hours ago`
    if (diffDays < 7) return `${diffDays} days ago`
    return formatDate(dateString)
  }

  const isAdmin = user.roles.some(r => r.toLowerCase() === "admin")

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="w-[560px]">
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-border-subtle">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-lg bg-neon-cyan/10 flex items-center justify-center">
              <User className="w-[18px] h-[18px] text-neon-cyan" />
            </div>
            <h2 className="text-lg font-semibold text-text-primary">User Details</h2>
          </div>
          <button onClick={onClose} className="text-text-muted hover:text-text-primary">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="p-6 space-y-5">
          {/* User Card */}
          <div className="p-4 rounded-xl bg-surface-elevated flex items-center gap-4">
            <Avatar name={displayName} src={user.avatarUrl} size="lg" className="flex-shrink-0" />
            <div className="space-y-1 w-0 flex-1">
              <h3 className="text-lg font-semibold text-text-primary truncate" title={displayName}>
                {displayName}
              </h3>
              <p className="text-sm font-mono text-text-muted truncate" title={user.email}>{user.email}</p>
              <div className="flex items-center gap-2 pt-1">
                <span className={cn(
                  "px-2 py-1 rounded text-[10px] font-mono font-medium",
                  user.isActive
                    ? "bg-neon-green/10 text-neon-green"
                    : "bg-neon-red/10 text-neon-red"
                )}>
                  {user.isActive ? "Active" : "Inactive"}
                </span>
                {isAdmin && (
                  <span className="px-2 py-1 rounded text-[10px] font-mono font-medium bg-neon-gold/10 text-neon-gold">
                    Admin
                  </span>
                )}
              </div>
            </div>
          </div>

          {/* Account Information */}
          <div className="space-y-3">
            <h4 className="text-sm font-semibold text-text-secondary">Account Information</h4>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1 min-w-0">
                <p className="text-[11px] text-text-dimmed">Username</p>
                <p className="text-sm font-mono text-text-primary truncate" title={user.userName || "—"}>
                  {user.userName || "—"}
                </p>
              </div>
              <div className="space-y-1 min-w-0">
                <p className="text-[11px] text-text-dimmed">Email</p>
                <p className="text-sm font-mono text-text-primary truncate" title={user.email}>
                  {user.email}
                </p>
              </div>
              <div className="space-y-1">
                <p className="text-[11px] text-text-dimmed">First Name</p>
                <p className="text-sm font-mono text-text-primary">
                  {user.name || "—"}
                </p>
              </div>
              <div className="space-y-1">
                <p className="text-[11px] text-text-dimmed">Last Name</p>
                <p className="text-sm font-mono text-text-primary">
                  {user.surname || "—"}
                </p>
              </div>
              <div className="space-y-1">
                <p className="text-[11px] text-text-dimmed">Phone Number</p>
                <p className="text-sm font-mono text-text-primary">
                  {user.phoneNumber || "—"}
                </p>
              </div>
              <div className="space-y-1">
                <p className="text-[11px] text-text-dimmed">Created At</p>
                <p className="text-sm font-mono text-text-primary">
                  {formatDate(user.createdAt)}
                </p>
              </div>
            </div>
          </div>

          {/* Assigned Roles */}
          <div className="space-y-2.5">
            <h4 className="text-sm font-semibold text-text-secondary">Assigned Roles</h4>
            <div className="flex flex-wrap gap-2">
              {user.roles.length === 0 ? (
                <span className="text-sm text-text-muted">-</span>
              ) : (
                user.roles.map(role => {
                  const isAdminRole = role.toLowerCase() === "admin"
                  return (
                    <span
                      key={role}
                      className={cn(
                        "inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md text-xs font-medium",
                        isAdminRole
                          ? "bg-neon-purple/10 text-neon-purple"
                          : "bg-neon-cyan/10 text-neon-cyan"
                      )}
                    >
                      {isAdminRole ? (
                        <Shield className="w-3 h-3" />
                      ) : (
                        <Users className="w-3 h-3" />
                      )}
                      {role}
                    </span>
                  )
                })
              )}
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-border-subtle">
          <Button
            onClick={() => {
              onClose()
              onEdit()
            }}
            className="gap-1.5"
          >
            <Pencil className="w-3.5 h-3.5" />
            Edit User
          </Button>
          <Button variant="secondary" onClick={onClose}>
            Close
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================
//  Invite User Modal
// ============================================================

interface InviteUserModalProps {
  open: boolean
  onClose: () => void
  roles: string[]
}

export const InviteUserModal: React.FC<InviteUserModalProps> = ({
  open,
  onClose,
  roles,
}) => {
  const [isLoading, setIsLoading] = useState(false)
  const [sent, setSent] = useState(false)
  const [formData, setFormData] = useState({
    email: "",
    role: "member",
    message: "",
  })

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)
    // Simulate API call
    await new Promise(resolve => setTimeout(resolve, 1000))
    setIsLoading(false)
    setSent(true)
  }

  const handleClose = () => {
    setSent(false)
    setFormData({ email: "", role: "member", message: "" })
    onClose()
  }

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="w-[480px]">
        {sent ? (
          // Success State
          <div className="p-8 text-center space-y-4">
            <div className="w-16 h-16 mx-auto rounded-full bg-neon-green/10 border border-neon-green/30 flex items-center justify-center">
              <ShieldCheck className="w-8 h-8 text-neon-green" />
            </div>
            <div className="space-y-2">
              <h2 className="text-lg font-semibold text-text-primary">Invitation Sent!</h2>
              <p className="text-sm text-text-muted">
                An invitation email has been sent to <span className="text-neon-cyan font-mono break-all">{formData.email}</span>
              </p>
            </div>
            <Button onClick={handleClose} className="w-full">
              Done
            </Button>
          </div>
        ) : (
          <form onSubmit={handleSubmit}>
            {/* Header */}
            <div className="flex items-center justify-between p-5 border-b border-border-subtle">
              <div className="space-y-1">
                <h2 className="text-lg font-semibold text-text-primary">Invite User</h2>
                <p className="text-xs text-text-muted">Send an invitation link via email</p>
              </div>
              <button type="button" onClick={handleClose} className="text-text-muted hover:text-text-primary">
                <X className="w-5 h-5" />
              </button>
            </div>

            {/* Body */}
            <div className="p-5 space-y-4">
              <div className="space-y-1.5">
                <label className="text-sm text-text-secondary">Email Address *</label>
                <Input
                  type="email"
                  value={formData.email}
                  onChange={(e) => setFormData(prev => ({ ...prev, email: e.target.value }))}
                  placeholder="user@example.com"
                  required
                />
              </div>

              <div className="space-y-1.5">
                <label className="text-sm text-text-secondary">Assign Role</label>
                <div className="flex flex-wrap gap-2">
                  {roles.map(role => (
                    <button
                      key={role}
                      type="button"
                      onClick={() => setFormData(prev => ({ ...prev, role }))}
                      className={cn(
                        "px-3 py-1.5 rounded-full text-xs border transition-all",
                        formData.role === role
                          ? "bg-neon-purple/15 border-neon-purple/40 text-neon-purple"
                          : "bg-surface border-border-subtle text-text-muted hover:border-border-default"
                      )}
                    >
                      {role}
                    </button>
                  ))}
                </div>
              </div>

              <div className="space-y-1.5">
                <label className="text-sm text-text-secondary">Personal Message (Optional)</label>
                <textarea
                  value={formData.message}
                  onChange={(e) => setFormData(prev => ({ ...prev, message: e.target.value }))}
                  placeholder="Add a personal message to the invitation..."
                  rows={3}
                  className="w-full px-3 py-2 text-sm bg-surface border border-border-subtle rounded-lg resize-none text-text-primary placeholder:text-text-dimmed focus:outline-none focus:border-neon-cyan/50"
                />
              </div>
            </div>

            {/* Footer */}
            <div className="flex justify-end gap-3 p-5 border-t border-border-subtle">
              <Button type="button" variant="secondary" onClick={handleClose}>
                Cancel
              </Button>
              <Button type="submit" disabled={isLoading || !formData.email} className="gap-1.5">
                <User className="w-4 h-4" />
                {isLoading ? "Sending..." : "Send Invitation"}
              </Button>
            </div>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}
