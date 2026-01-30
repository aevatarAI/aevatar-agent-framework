import React, { useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { ArrowLeft, CheckCircle } from "lucide-react"
import { AuthLayout } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { PasswordInput } from "@/components/ui/input"
import { useToast } from "@/components/ui/toast"
import { abpResetPassword } from "@/lib/abp"

// ============================================================
//  Reset Password Page - Set New Password
// ============================================================

export default function ResetPasswordPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const { error: showError } = useToast()
  const token = searchParams.get("resetToken") || ""
  const userId = searchParams.get("userId") || ""
  
  const [password, setPassword] = useState("")
  const [confirmPassword, setConfirmPassword] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [isSuccess, setIsSuccess] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    // Validate passwords match
    if (password !== confirmPassword) {
      showError("Passwords do not match")
      return
    }

    // ABP Password Policy Validation
    const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]).{6,}$/
    if (!passwordRegex.test(password)) {
      showError("Password must be at least 6 characters with uppercase, lowercase, number & special character")
      return
    }

    setIsLoading(true)

    try {
      const result = await abpResetPassword(userId, token, password)
      
      if (result.success) {
        setIsSuccess(true)
      } else {
        showError(result.error || "Failed to reset password")
      }
    } catch {
      showError("An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
  }

  if (isSuccess) {
    return (
      <AuthLayout
        title="Password Reset"
        subtitle="Your password has been successfully reset"
      >
        <div className="text-center space-y-6">
          {/* Success Icon */}
          <div className="mx-auto w-16 h-16 rounded-full bg-neon-green/10 border border-neon-green/30 flex items-center justify-center">
            <CheckCircle className="w-8 h-8 text-neon-green" />
          </div>

          <div className="space-y-2">
            <p className="text-text-secondary">
              Your password has been updated successfully.
            </p>
            <p className="text-sm text-text-muted">
              You can now sign in with your new password.
            </p>
          </div>

          <Button
            className="w-full"
            onClick={() => navigate("/login")}
          >
            Continue to Sign In
          </Button>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Reset Password"
      subtitle="Create a new password for your account"
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {/* New Password Field */}
        <div className="space-y-2">
          <label htmlFor="password" className="text-sm font-medium text-text-secondary">
            New Password
          </label>
          <PasswordInput
            id="password"
            placeholder="Enter new password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            disabled={isLoading}
          />
          <p className="text-xs text-text-dimmed">
            Min 6 characters with uppercase, lowercase, number & special character
          </p>
        </div>

        {/* Confirm Password Field */}
        <div className="space-y-2">
          <label htmlFor="confirmPassword" className="text-sm font-medium text-text-secondary">
            Confirm New Password
          </label>
          <PasswordInput
            id="confirmPassword"
            placeholder="Confirm new password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            required
            disabled={isLoading}
          />
        </div>

        {/* Submit Button - Green per design */}
        <Button
          type="submit"
          variant="success"
          className="w-full"
          disabled={isLoading}
        >
          {isLoading ? "Resetting..." : "Reset Password"}
        </Button>

        {/* Back to Login */}
        <div className="text-center">
          <Link
            to="/login"
            className="inline-flex items-center gap-2 text-sm text-text-muted hover:text-neon-cyan transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Back to sign in
          </Link>
        </div>
      </form>
    </AuthLayout>
  )
}
