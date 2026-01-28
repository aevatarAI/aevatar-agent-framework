import React, { useState } from "react"
import { Button } from "@/components/ui/button"
import { PasswordInput } from "@/components/ui/input"
import { Card, CardHeader, CardTitle, CardContent } from "@/components/ui/card"

// ============================================================
//  Password Panel - Change Password
// ============================================================

export const PasswordPanel: React.FC = () => {
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState("")
  const [success, setSuccess] = useState(false)
  const [formData, setFormData] = useState({
    currentPassword: "",
    newPassword: "",
    confirmPassword: "",
  })

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError("")
    setSuccess(false)

    // Validation
    if (formData.newPassword !== formData.confirmPassword) {
      setError("New passwords do not match")
      return
    }

    // ABP Password Policy Validation
    const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]).{6,}$/
    if (!passwordRegex.test(formData.newPassword)) {
      setError("Password must be at least 6 characters and contain uppercase, lowercase, number, and special character")
      return
    }

    setIsLoading(true)
    try {
      // Simulate API call
      await new Promise(resolve => setTimeout(resolve, 800))
      setSuccess(true)
      setFormData({ currentPassword: "", newPassword: "", confirmPassword: "" })
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
            {error && (
              <div className="p-3 rounded-lg bg-neon-red/10 border border-neon-red/30 text-neon-red text-sm">
                {error}
              </div>
            )}

            {success && (
              <div className="p-3 rounded-lg bg-neon-green/10 border border-neon-green/30 text-neon-green text-sm">
                Password changed successfully!
              </div>
            )}

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
