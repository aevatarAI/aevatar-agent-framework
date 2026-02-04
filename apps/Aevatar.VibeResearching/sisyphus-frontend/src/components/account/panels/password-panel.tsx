import React, { useState } from "react"
import { Button } from "@/components/ui/button"
import { PasswordInput } from "@/components/ui/input"
import { Card, CardHeader, CardTitle, CardContent } from "@/components/ui/card"
import { changePassword } from "@/lib/abp"
import { useToast } from "@/components/ui/toast"

// ============================================================
//  Password Panel - Change Password
// ============================================================

export const PasswordPanel: React.FC = () => {
  const [isLoading, setIsLoading] = useState(false)
  const { error: showError, success: showSuccess } = useToast()
  const [formData, setFormData] = useState({
    currentPassword: "",
    newPassword: "",
    confirmPassword: "",
  })

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    // Validation
    if (formData.newPassword !== formData.confirmPassword) {
      showError("New passwords do not match")
      return
    }

    // ABP Password Policy Validation
    const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]).{6,}$/
    if (!passwordRegex.test(formData.newPassword)) {
      showError("Password must be at least 6 characters and contain uppercase, lowercase, number, and special character")
      return
    }

    setIsLoading(true)
    try {
      const result = await changePassword({
        currentPassword: formData.currentPassword,
        newPassword: formData.newPassword,
      })

      if (result.ok) {
        showSuccess("Password changed successfully!")
        setFormData({ currentPassword: "", newPassword: "", confirmPassword: "" })
      } else {
        // Handle validation details if available
        if (result.details && result.details.length > 0) {
          showError(result.details.join(", "))
        } else {
          showError(result.error || "Failed to change password")
        }
      }
    } catch (err) {
      console.error('[Password] Change password error:', err)
      showError(err instanceof Error ? err.message : "An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="max-w-lg">
      <Card>
        <CardHeader>
          <CardTitle>Change Password</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">Current Password</label>
              <PasswordInput
                value={formData.currentPassword}
                onChange={(e) => setFormData(prev => ({ ...prev, currentPassword: e.target.value }))}
                placeholder="Enter current password"
                required
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">New Password</label>
              <PasswordInput
                value={formData.newPassword}
                onChange={(e) => setFormData(prev => ({ ...prev, newPassword: e.target.value }))}
                placeholder="Enter new password"
                required
              />
              <p className="text-xs text-text-dimmed">
                Min 6 characters with uppercase, lowercase, number & special character
              </p>
            </div>

            <div className="space-y-1.5">
              <label className="text-sm text-text-secondary">Confirm New Password</label>
              <PasswordInput
                value={formData.confirmPassword}
                onChange={(e) => setFormData(prev => ({ ...prev, confirmPassword: e.target.value }))}
                placeholder="Confirm new password"
                required
              />
            </div>

            <div className="flex justify-end pt-4">
              <Button type="submit" disabled={isLoading}>
                {isLoading ? "Updating..." : "Update Password"}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
