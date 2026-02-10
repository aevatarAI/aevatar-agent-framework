import React, { useState } from "react"
import { Pencil, Lock, User, Camera, Save, X } from "lucide-react"
import { useAuthStore, useHasPassword } from "@/store/auth-store"
import { useNavigate } from "react-router-dom"
import { AvatarUploadModal, AvatarCropModal } from "../avatar-modals"
import { updateMyProfile, uploadProfilePicture, getProfilePictureUrl } from "@/lib/abp"

// ============================================================
//  Profile Panel - With Edit & Avatar Upload
// ============================================================

export const ProfilePanel: React.FC = () => {
  const { user, updateUser: updateAuthUser } = useAuthStore()
  const navigate = useNavigate()
  const hasPassword = useHasPassword()

  // Edit mode state
  const [isEditing, setIsEditing] = useState(false)
  const [formData, setFormData] = useState({
    userName: user?.userName || "",
    firstName: user?.name || "",
    lastName: user?.surname || "",
    email: user?.email || "",
    phone: user?.phoneNumber || "",
  })

  // Avatar upload state
  const [showAvatarUpload, setShowAvatarUpload] = useState(false)
  const [showAvatarCrop, setShowAvatarCrop] = useState(false)
  const [selectedImage, setSelectedImage] = useState<string | null>(null)
  const [avatarUrl, setAvatarUrl] = useState<string | null>(
    user?.id ? getProfilePictureUrl(user.id) : null
  )
  const [isUploadingAvatar, setIsUploadingAvatar] = useState(false)
  const [avatarError, setAvatarError] = useState<string | null>(null)
  
  // Save state
  const [isSaving, setIsSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)

  const fullName = formData.firstName || formData.lastName 
    ? `${formData.firstName} ${formData.lastName}`.trim() 
    : "-"
  const isAdmin = user?.isAdmin || user?.roles?.includes("admin")

  const handleInputChange = (field: keyof typeof formData, value: string) => {
    setFormData(prev => ({ ...prev, [field]: value }))
  }

  const handleSave = async () => {
    setIsSaving(true)
    setSaveError(null)

    try {
      // Update profile via user-accessible API (not admin API)
      const result = await updateMyProfile({
        name: formData.firstName.trim() || undefined,
        surname: formData.lastName.trim() || undefined,
        phoneNumber: formData.phone.trim() || undefined,
      })

      if (!result.ok) {
        setSaveError(result.error || 'Failed to update profile')
        return
      }

      // Update auth store with new user data
      updateAuthUser({
        name: formData.firstName.trim(),
        surname: formData.lastName.trim(),
        phoneNumber: formData.phone.trim(),
      })

      setIsEditing(false)
      setSaveError(null)
    } catch (error) {
      console.error('[Profile] Update profile error:', error)
      setSaveError(error instanceof Error ? error.message : 'Failed to update profile')
    } finally {
      setIsSaving(false)
    }
  }

  const handleCancel = () => {
    setSaveError(null)
    setFormData({
      userName: user?.userName || "",
      firstName: user?.name || "",
      lastName: user?.surname || "",
      email: user?.email || "",
      phone: user?.phoneNumber || "",
    })
    setIsEditing(false)
  }

  const handleAvatarUpload = (file: File) => {
    // Validate file size (max 2MB)
    const MAX_SIZE = 2 * 1024 * 1024
    if (file.size > MAX_SIZE) {
      setAvatarError('File too large. Maximum size is 2MB.')
      setShowAvatarUpload(false)
      return
    }

    // Validate file type
    const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp']
    if (!ALLOWED_TYPES.includes(file.type)) {
      setAvatarError('Invalid file type. Allowed: JPG, PNG, WebP.')
      setShowAvatarUpload(false)
      return
    }

    setAvatarError(null)
    const url = URL.createObjectURL(file)
    setSelectedImage(url)
    setShowAvatarUpload(false)
    setShowAvatarCrop(true)
  }

  const handleAvatarSave = async (blob: Blob) => {
    setIsUploadingAvatar(true)
    setAvatarError(null)

    try {
      // Convert blob to File for upload
      const file = new File([blob], 'avatar.png', { type: blob.type || 'image/png' })
      
      const result = await uploadProfilePicture(file)
      
      if (!result.ok) {
        setAvatarError(result.error || 'Failed to upload avatar')
        return
      }

      // Update avatar URL with cache buster
      if (user?.id) {
        setAvatarUrl(`${getProfilePictureUrl(user.id)}?t=${Date.now()}`)
      }
      
      setShowAvatarCrop(false)
      setSelectedImage(null)
    } catch (error) {
      console.error('[Profile] Avatar upload error:', error)
      setAvatarError(error instanceof Error ? error.message : 'Failed to upload avatar')
    } finally {
      setIsUploadingAvatar(false)
    }
  }

  return (
    <div className="max-w-2xl space-y-6">
      {/* Personal Information Card */}
      <div className="rounded-xl bg-surface border border-border-subtle p-6 space-y-5">
        {/* Header */}
        <div className="flex items-center justify-between">
          <h3 className="text-base font-semibold text-text-primary">Personal Information</h3>
          {!isEditing ? (
            <button
              onClick={() => setIsEditing(true)}
              className="flex items-center gap-1.5 px-3 py-1.5 text-xs text-text-secondary border border-border-subtle rounded-md hover:border-border-default transition-colors"
            >
              <Pencil className="w-3 h-3" />
              Edit
            </button>
          ) : (
            <div className="flex items-center gap-2">
              <button
                onClick={handleCancel}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs text-text-secondary border border-border-subtle rounded-md hover:border-border-default transition-colors"
              >
                <X className="w-3 h-3" />
                Cancel
              </button>
              <button
                onClick={handleSave}
                disabled={isSaving}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs text-bg-base bg-neon-cyan rounded-md hover:bg-neon-cyan/90 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <Save className={`w-3 h-3 ${isSaving ? "animate-spin" : ""}`} />
                {isSaving ? "Saving..." : "Save"}
              </button>
            </div>
          )}
        </div>

        {/* Avatar Row - Only show when NOT editing */}
        {!isEditing && (
          <div className="flex items-center gap-3">
            {/* Avatar with Camera Badge */}
            <button
              onClick={() => setShowAvatarUpload(true)}
              disabled={isUploadingAvatar}
              className="relative group cursor-pointer disabled:cursor-not-allowed"
            >
              {/* Avatar Circle */}
              <div className={`w-[72px] h-[72px] rounded-full bg-neon-cyan/20 border-2 border-neon-cyan flex items-center justify-center overflow-hidden group-hover:border-neon-cyan/80 transition-colors ${isUploadingAvatar ? 'opacity-50' : ''}`}>
                {avatarUrl ? (
                  <img src={avatarUrl} alt="Avatar" className="w-full h-full object-cover" onError={() => setAvatarUrl(null)} />
                ) : (
                  <User className="w-8 h-8 text-neon-cyan" />
                )}
                {/* Loading Overlay */}
                {isUploadingAvatar && (
                  <div className="absolute inset-0 flex items-center justify-center bg-bg-base/50">
                    <div className="w-6 h-6 border-2 border-neon-cyan border-t-transparent rounded-full animate-spin" />
                  </div>
                )}
              </div>
              {/* Camera Badge - Always Visible */}
              <div className="absolute -bottom-0.5 -right-0.5 w-7 h-7 rounded-full bg-surface border-[1.5px] border-neon-cyan flex items-center justify-center group-hover:bg-neon-cyan/20 transition-colors">
                <Camera className="w-3.5 h-3.5 text-neon-cyan" />
              </div>
            </button>
            <div className="space-y-1">
              <h4 className="text-lg font-semibold text-text-primary">{fullName}</h4>
              <p className="text-[13px] font-mono text-text-muted">{formData.email}</p>
              {isAdmin && (
                <span className="inline-block px-2 py-0.5 text-[10px] font-mono font-medium text-neon-purple bg-neon-purple/10 rounded-full">
                  Admin
                </span>
              )}
            </div>
          </div>
        )}

        {/* Error Messages */}
        {(saveError || avatarError) && (
          <div className="p-3 rounded-lg bg-neon-red/10 border border-neon-red/30 text-neon-red text-sm">
            {saveError || avatarError}
          </div>
        )}

        {/* Fields - Edit Mode */}
        {isEditing ? (
          <div className="space-y-4">
            {/* User Name - Read Only */}
            <div className="space-y-1.5">
              <label className="text-[13px] font-medium text-text-secondary">
                User Name
              </label>
              <input
                type="text"
                value={formData.userName}
                disabled
                className="w-full h-10 px-3 text-[13px] font-mono bg-background/50 border border-border-subtle rounded-lg text-text-muted cursor-not-allowed"
              />
              <p className="text-[11px] text-text-dimmed">User name cannot be changed</p>
            </div>

            {/* First Name + Last Name Row */}
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <label className="text-[13px] font-medium text-text-secondary">First Name</label>
                <input
                  type="text"
                  value={formData.firstName}
                  onChange={(e) => handleInputChange("firstName", e.target.value)}
                  className="w-full h-10 px-3 text-[13px] bg-background border border-border-subtle rounded-lg text-text-primary focus:outline-none focus:border-neon-cyan transition-colors"
                />
              </div>
              <div className="space-y-1.5">
                <label className="text-[13px] font-medium text-text-secondary">Last Name</label>
                <input
                  type="text"
                  value={formData.lastName}
                  onChange={(e) => handleInputChange("lastName", e.target.value)}
                  className="w-full h-10 px-3 text-[13px] bg-background border border-border-subtle rounded-lg text-text-primary focus:outline-none focus:border-neon-cyan transition-colors"
                />
              </div>
            </div>

            {/* Email Address - Read Only */}
            <div className="space-y-1.5">
              <label className="text-[13px] font-medium text-text-secondary">
                Email Address
              </label>
              <input
                type="email"
                value={formData.email}
                disabled
                className="w-full h-10 px-3 text-[13px] font-mono bg-background/50 border border-border-subtle rounded-lg text-text-muted cursor-not-allowed"
              />
              <p className="text-[11px] text-text-dimmed">Email cannot be changed</p>
            </div>

            {/* Phone Number */}
            <div className="space-y-1.5">
              <label className="text-[13px] font-medium text-text-secondary">Phone Number</label>
              <input
                type="tel"
                value={formData.phone}
                onChange={(e) => handleInputChange("phone", e.target.value)}
                className="w-full h-10 px-3 text-[13px] font-mono bg-background border border-border-subtle rounded-lg text-text-primary focus:outline-none focus:border-neon-cyan transition-colors"
              />
            </div>
          </div>
        ) : (
          /* Fields - View Mode */
          <div className="grid grid-cols-2 gap-x-6 gap-y-4">
            <div className="space-y-1">
              <p className="text-xs text-text-dimmed">User Name</p>
              <p className="text-sm font-mono text-text-primary">{formData.userName || "-"}</p>
            </div>
            <div className="space-y-1">
              <p className="text-xs text-text-dimmed">Full Name</p>
              <p className="text-sm text-text-primary">{fullName}</p>
            </div>
            <div className="space-y-1">
              <p className="text-xs text-text-dimmed">Email Address</p>
              <p className="text-sm font-mono text-text-primary">{formData.email || "-"}</p>
            </div>
            <div className="space-y-1">
              <p className="text-xs text-text-dimmed">Phone Number</p>
              <p className="text-sm font-mono text-text-primary">{formData.phone || "-"}</p>
            </div>
          </div>
        )}
      </div>

      {/* Security Settings Card */}
      {/* Security Settings — hide for OAuth users who have no password */}
      {hasPassword && (
        <div className="rounded-xl bg-surface border border-border-subtle p-6 space-y-4">
          <h3 className="text-base font-semibold text-text-primary">Security Settings</h3>

          {/* Password Row */}
          <div className="flex items-center justify-between p-4 rounded-lg bg-surface-elevated">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-neon-gold/10 flex items-center justify-center">
                  <Lock className="w-5 h-5 text-neon-gold" />
                </div>
                <div className="space-y-0.5">
                  <p className="text-sm font-medium text-text-primary">Password</p>
                </div>
              </div>
            <button
              onClick={() => navigate("/account/password")}
              className="px-3.5 py-2 text-xs font-medium text-bg-base bg-neon-gold rounded-md hover:bg-neon-gold/90 transition-colors"
            >
              Change
            </button>
          </div>
        </div>
      )}

      {/* Avatar Upload Modal */}
      <AvatarUploadModal
        open={showAvatarUpload}
        onClose={() => setShowAvatarUpload(false)}
        onUpload={handleAvatarUpload}
      />

      {/* Avatar Crop Modal */}
      {selectedImage && (
        <AvatarCropModal
          open={showAvatarCrop}
          onClose={() => {
            setShowAvatarCrop(false)
            setSelectedImage(null)
          }}
          onSave={handleAvatarSave}
          imageUrl={selectedImage}
        />
      )}
    </div>
  )
}
