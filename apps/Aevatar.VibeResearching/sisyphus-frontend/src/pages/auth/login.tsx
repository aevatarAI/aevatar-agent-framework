import React, { useState, useEffect } from "react"
import { Link, useNavigate } from "react-router-dom"
import { LogIn, Loader2, User } from "lucide-react"
import { AuthLayout, SocialLoginButtons } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { Input, PasswordInput } from "@/components/ui/input"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { useToast } from "@/components/ui/toast"
import { abpLogin, type AuthResponse } from "@/lib/abp"
import { useAuthStore } from "@/store/auth-store"
import {
  isGoogleConfigured,
  isGitHubConfigured,
  initiateGoogleLogin,
  initiateGitHubLogin,
} from "@/lib/oauth"

// Demo credentials for testing (remove in production)
const getDemoCredentials = () => ({
  admin: { email: "admin", password: "1q2w3E*" },  // ABP default
  user: { email: "testuser", password: "Abc@123" },
})

// OAuth feature flag - set VITE_ENABLE_OAUTH=false to disable
const OAUTH_ENABLED = import.meta.env.VITE_ENABLE_OAUTH !== 'false'

// ============================================================
//  Login Page - Email/Password + OAuth Social Login
//  Both Google and GitHub use redirect flow for security
// ============================================================

export default function LoginPage() {
  const navigate = useNavigate()
  const { login } = useAuthStore()
  const { error: showError } = useToast()
  
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [rememberMe, setRememberMe] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [oauthStatus, setOauthStatus] = useState<string | null>(null)

  // Check OAuth configuration on mount
  useEffect(() => {
    const googleConfigured = isGoogleConfigured()
    const githubConfigured = isGitHubConfigured()
    
    if (!googleConfigured && !githubConfigured) {
      setOauthStatus(null) // Hide status when no OAuth configured
    } else {
      const configured = []
      if (googleConfigured) configured.push("Google")
      if (githubConfigured) configured.push("GitHub")
      setOauthStatus(`${configured.join(" & ")} OAuth ready`)
    }
  }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)

    try {
      const result: AuthResponse = await abpLogin({ email, password, rememberMe })
      
      if (result.success && result.user) {
        login(result.user)
        navigate("/app")
      } else {
        showError(result.error || "Login failed")
      }
    } catch {
      showError("An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
  }

  const handleGoogleLogin = () => {
    if (isGoogleConfigured()) {
      // Google OAuth - redirect flow (same as GitHub)
      setIsLoading(true)
      initiateGoogleLogin()
      // Page will redirect to Google, callback handled by oauth-callback page
    } else {
      showError("Google OAuth not configured. Please use email/password login.")
    }
  }

  const handleGithubLogin = () => {
    if (isGitHubConfigured()) {
      // GitHub OAuth - redirect flow
      setIsLoading(true)
      initiateGitHubLogin()
      // Page will redirect to GitHub, callback handled by oauth-callback page
    } else {
      showError("GitHub OAuth not configured. Please use email/password login.")
    }
  }

  const fillDemoCredentials = (type: "admin" | "user") => {
    const creds = getDemoCredentials()[type]
    setEmail(creds.email)
    setPassword(creds.password)
  }

  return (
    <AuthLayout
      title="Welcome Back"
      subtitle="Sign in to continue your research journey"
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {/* OAuth Status (dev info) - Only show when OAuth enabled */}
        {OAUTH_ENABLED && oauthStatus && import.meta.env.DEV && (
          <div className="p-2 rounded-lg bg-neon-cyan/10 border border-neon-cyan/30 text-neon-cyan text-xs text-center">
            {oauthStatus}
          </div>
        )}

        {/* Username/Email Field */}
        <div className="space-y-2">
          <label htmlFor="email" className="text-sm font-medium text-text-secondary">
            Username or Email
          </label>
          <Input
            id="email"
            type="text"
            icon={User}
            placeholder="Enter username or email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
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
            placeholder="Enter your password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            disabled={isLoading}
          />
        </div>

        {/* Remember Me + Forgot Password (same row per design) */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Checkbox
              id="rememberMe"
              checked={rememberMe}
              onCheckedChange={(checked) => setRememberMe(checked === true)}
            />
            <Label htmlFor="rememberMe" className="text-sm text-text-secondary cursor-pointer">
              Remember me
            </Label>
          </div>
          <Link
            to="/forgot-password"
            className="text-sm text-neon-cyan hover:underline"
          >
            Forgot password?
          </Link>
        </div>

        {/* Submit Button */}
        <Button
          type="submit"
          className="w-full gap-2"
          disabled={isLoading}
        >
          {isLoading ? (
            <Loader2 className="w-[18px] h-[18px] animate-spin" />
          ) : (
            <LogIn className="w-[18px] h-[18px]" />
          )}
          {isLoading ? "Signing in..." : "Sign In"}
        </Button>

        {/* Social Login - Controlled by VITE_ENABLE_OAUTH */}
        {OAUTH_ENABLED && (
          <SocialLoginButtons
            onGoogleLogin={handleGoogleLogin}
            onGithubLogin={handleGithubLogin}
            isLoading={isLoading}
          />
        )}

        {/* Register Link */}
        <p className="text-center text-sm text-text-muted">
          Don't have an account?{" "}
          <Link to="/register" className="text-neon-cyan hover:underline font-medium">
            Sign up
          </Link>
        </p>

        {/* Demo Credentials */}
        <div className="pt-4 border-t border-border-subtle">
          <p className="text-xs text-text-dimmed text-center mb-2">
            Demo Credentials
          </p>
          <div className="flex gap-2 justify-center">
            <button
              type="button"
              onClick={() => fillDemoCredentials("admin")}
              className="text-xs px-3 py-1.5 rounded bg-neon-gold/10 text-neon-gold border border-neon-gold/30 hover:bg-neon-gold/20 transition-colors"
            >
              Admin
            </button>
            <button
              type="button"
              onClick={() => fillDemoCredentials("user")}
              className="text-xs px-3 py-1.5 rounded bg-neon-cyan/10 text-neon-cyan border border-neon-cyan/30 hover:bg-neon-cyan/20 transition-colors"
            >
              User
            </button>
          </div>
        </div>
      </form>
    </AuthLayout>
  )
}
