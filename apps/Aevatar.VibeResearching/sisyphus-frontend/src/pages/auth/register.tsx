import React, { useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { User, UserPlus } from "lucide-react"
import { AuthLayout, SocialLoginButtons } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { Input, EmailInput, PasswordInput } from "@/components/ui/input"
import { useToast } from "@/components/ui/toast"
import { abpRegister, type AuthResponse } from "@/lib/abp"
import { useAuthStore } from "@/store/auth-store"

// OAuth feature flag - set VITE_ENABLE_OAUTH=false to disable
const OAUTH_ENABLED = import.meta.env.VITE_ENABLE_OAUTH !== 'false'

// ============================================================
//  Register Page - Create New Account
// ============================================================

export default function RegisterPage() {
  const navigate = useNavigate()
  const { login } = useAuthStore()
  const { error: showError } = useToast()
  
  const [formData, setFormData] = useState({
    userName: "",
    email: "",
    password: "",
    confirmPassword: "",
  })
  const [isLoading, setIsLoading] = useState(false)

  const updateField = (field: string, value: string) => {
    setFormData(prev => ({ ...prev, [field]: value }))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    // ABP UserName Validation
    const trimmedUserName = formData.userName.trim()
    if (!trimmedUserName) {
      showError("UserName is required")
      return
    }
    
    // ABP Identity userName rules: max 256 chars, allowed: letters, numbers, underscore, hyphen, dot
    const userNameRegex = /^[a-zA-Z0-9_.-]+$/
    if (!userNameRegex.test(trimmedUserName)) {
      showError("UserName can only contain letters, numbers, underscore, hyphen, and dot")
      return
    }
    
    if (trimmedUserName.length > 256) {
      showError("UserName must be 256 characters or less")
      return
    }

    // Validate passwords match
    if (formData.password !== formData.confirmPassword) {
      showError("Passwords do not match")
      return
    }

    // ABP Password Policy Validation
    const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]).{6,}$/
    if (!passwordRegex.test(formData.password)) {
      showError("Password must be at least 6 characters with uppercase, lowercase, number & special character")
      return
    }

    setIsLoading(true)

    try {
      const result: AuthResponse = await abpRegister({
        userName: formData.userName.trim(),
        email: formData.email,
        password: formData.password,
      })
      
      if (result.success && result.user) {
        login(result.user)
        navigate("/email-confirmation", { state: { email: formData.email } })
      } else {
        showError(result.error || "Registration failed")
      }
    } catch {
      showError("An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
  }

  const handleSocialLogin = async (_provider: "google" | "github") => {
    // TODO: Implement ABP external login flow
    showError("Social login is not yet configured. Please use email registration.")
  }

  return (
    <AuthLayout
      title="Create Account"
      subtitle="Join the AI-powered research revolution"
      variant="register"
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {/* UserName Field */}
        <div className="space-y-2">
          <label htmlFor="userName" className="text-sm font-medium text-text-secondary">
            UserName
          </label>
          <Input
            id="userName"
            type="text"
            icon={User}
            placeholder="Enter your username"
            value={formData.userName}
            onChange={(e) => updateField("userName", e.target.value)}
            required
            disabled={isLoading}
            maxLength={256}
          />
          <p className="text-xs text-text-dimmed">
            Letters, numbers, underscore, hyphen, and dot only. Max 256 characters.
          </p>
        </div>

        {/* Email Field */}
        <div className="space-y-2">
          <label htmlFor="email" className="text-sm font-medium text-text-secondary">
            Email
          </label>
          <EmailInput
            id="email"
            placeholder="Enter your email"
            value={formData.email}
            onChange={(e) => updateField("email", e.target.value)}
            required
            disabled={isLoading}
          />
        </div>

        {/* Password Field */}
        <div className="space-y-2">
          <label htmlFor="password" className="text-sm font-medium text-text-secondary">
            Password
          </label>
          <PasswordInput
            id="password"
            placeholder="Create a password"
            value={formData.password}
            onChange={(e) => updateField("password", e.target.value)}
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
            Confirm Password
          </label>
          <PasswordInput
            id="confirmPassword"
            placeholder="Confirm your password"
            value={formData.confirmPassword}
            onChange={(e) => updateField("confirmPassword", e.target.value)}
            required
            disabled={isLoading}
          />
        </div>

        {/* Submit Button */}
        <Button
          type="submit"
          variant="gold"
          className="w-full gap-2"
          disabled={isLoading}
        >
          <UserPlus className="w-[18px] h-[18px]" />
          {isLoading ? "Creating account..." : "Create Account"}
        </Button>

        {/* Social Login - Controlled by VITE_ENABLE_OAUTH */}
        {OAUTH_ENABLED && (
          <SocialLoginButtons
            onGoogleLogin={() => handleSocialLogin("google")}
            onGithubLogin={() => handleSocialLogin("github")}
            isLoading={isLoading}
          />
        )}

        {/* Login Link */}
        <p className="text-center text-sm text-text-muted">
          Already have an account?{" "}
          <Link to="/login" className="text-neon-cyan hover:underline font-medium">
            Sign in
          </Link>
        </p>
      </form>
    </AuthLayout>
  )
}
