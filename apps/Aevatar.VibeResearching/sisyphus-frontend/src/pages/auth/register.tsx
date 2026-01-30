import React, { useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { User, UserPlus } from "lucide-react"
import { AuthLayout, SocialLoginButtons } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { Input, EmailInput, PasswordInput } from "@/components/ui/input"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
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
  
  const [formData, setFormData] = useState({
    userName: "",
    email: "",
    password: "",
  })
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [error, setError] = useState("")
  const [isLoading, setIsLoading] = useState(false)

  const updateField = (field: string, value: string) => {
    setFormData(prev => ({ ...prev, [field]: value }))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError("")

    // ABP UserName Validation
    const trimmedUserName = formData.userName.trim()
    if (!trimmedUserName) {
      setError("UserName is required")
      return
    }
    
    // ABP Identity userName rules: max 256 chars, allowed: letters, numbers, underscore, hyphen, dot
    // Regex: allows a-z, A-Z, 0-9, _, -, .
    const userNameRegex = /^[a-zA-Z0-9_.-]+$/
    if (!userNameRegex.test(trimmedUserName)) {
      setError("UserName can only contain letters, numbers, underscore, hyphen, and dot")
      return
    }
    
    if (trimmedUserName.length > 256) {
      setError("UserName must be 256 characters or less")
      return
    }

    // ABP Password Policy Validation
    const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]).{6,}$/
    if (!passwordRegex.test(formData.password)) {
      setError("Password must be at least 6 characters with uppercase, lowercase, number & special character")
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
        setError(result.error || "Registration failed")
      }
    } catch (err) {
      setError("An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
  }

  const handleSocialLogin = async (_provider: "google" | "github") => {
    // TODO: Implement ABP external login flow
    // ABP external login requires server-side OAuth flow
    setError("Social login is not yet configured. Please use email registration.")
  }

  return (
    <AuthLayout
      title="Create Account"
      subtitle="Join the AI-powered research revolution"
      variant="register"
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {/* Error Message */}
        {error && (
          <div className="p-3 rounded-lg bg-neon-red/10 border border-neon-red/30 text-neon-red text-sm">
            {error}
          </div>
        )}

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

        {/* Terms Agreement */}
        <div className="flex items-start gap-2">
          <Checkbox
            id="terms"
            checked={termsAccepted}
            onCheckedChange={(checked) => setTermsAccepted(checked === true)}
            required
            className="mt-0.5"
          />
          <Label htmlFor="terms" className="text-sm text-text-secondary cursor-pointer">
            I agree to the{" "}
            <a href="#" className="text-neon-cyan hover:underline">
              Terms of Service
            </a>{" "}
            and{" "}
            <a href="#" className="text-neon-cyan hover:underline">
              Privacy Policy
            </a>
          </Label>
        </div>

        {/* Submit Button */}
        <Button
          type="submit"
          variant="gold"
          className="w-full gap-2"
          disabled={isLoading || !termsAccepted}
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
