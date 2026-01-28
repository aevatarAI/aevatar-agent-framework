import React, { useState, useEffect } from "react"
import { Link, useNavigate } from "react-router-dom"
import { LogIn, Loader2 } from "lucide-react"
import { AuthLayout, SocialLoginButtons } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { EmailInput, PasswordInput } from "@/components/ui/input"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { mockLogin, mockSocialLogin, getDemoCredentials } from "@/lib/mock/auth"
import { useAuthStore } from "@/store/auth-store"
import {
  isGoogleConfigured,
  isGitHubConfigured,
  signInWithGoogle,
  initiateGitHubLogin,
  mockGitHubLogin,
  oauthConfig,
} from "@/lib/oauth"
import type { OAuthUser } from "@/lib/oauth"

// ============================================================
//  Login Page - Email/Password + OAuth Social Login
// ============================================================

export default function LoginPage() {
  const navigate = useNavigate()
  const { login } = useAuthStore()
  
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [rememberMe, setRememberMe] = useState(false)
  const [error, setError] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [oauthStatus, setOauthStatus] = useState<string | null>(null)

  // Check OAuth configuration on mount
  useEffect(() => {
    const googleConfigured = isGoogleConfigured()
    const githubConfigured = isGitHubConfigured()
    
    if (!googleConfigured && !githubConfigured) {
      setOauthStatus("OAuth not configured - using mock login")
    } else {
      const configured = []
      if (googleConfigured) configured.push("Google")
      if (githubConfigured) configured.push("GitHub")
      setOauthStatus(`${configured.join(" & ")} OAuth ready`)
    }
  }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError("")
    setIsLoading(true)

    try {
      const result = await mockLogin({ email, password, rememberMe })
      
      if (result.success && result.user) {
        login(result.user)
        navigate("/app")
      } else {
        setError(result.error || "Login failed")
      }
    } catch {
      setError("An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
  }

  // Convert OAuth user to AuthUser format
  const convertOAuthUser = (oauthUser: OAuthUser) => ({
    id: oauthUser.id,
    userName: oauthUser.email.split('@')[0],
    email: oauthUser.email,
    name: oauthUser.name.split(' ')[0] || oauthUser.name,
    surname: oauthUser.name.split(' ').slice(1).join(' ') || undefined,
    roles: ['member'],
    isAdmin: false,
    avatarUrl: oauthUser.picture,
  })

  const handleGoogleLogin = async () => {
    setError("")
    setIsLoading(true)

    try {
      if (isGoogleConfigured()) {
        // Real Google OAuth with FedCM
        const result = await signInWithGoogle()
        
        if (result.success && result.user) {
          login(convertOAuthUser(result.user))
          navigate("/app")
        } else if (result.error && oauthConfig.features.enableMockAuth) {
          // One Tap failed, fallback to mock in dev mode
          console.info('[OAuth] One Tap failed, using mock login in dev mode')
          const mockResult = await mockSocialLogin("google")
          if (mockResult.success && mockResult.user) {
            login(mockResult.user)
            navigate("/app")
          } else {
            setError(mockResult.error || "Google login failed")
          }
        } else {
          // Show user-friendly message
          const friendlyError = result.error?.includes('unavailable') || result.error?.includes('skipped')
            ? "Please sign in to Google in your browser first, or use email/password login."
            : (result.error || "Google login failed")
          setError(friendlyError)
        }
      } else {
        // Fallback to mock when not configured
        const result = await mockSocialLogin("google")
        if (result.success && result.user) {
          login(result.user)
          navigate("/app")
        } else {
          setError(result.error || "Google login failed")
        }
      }
    } catch {
      setError("An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
  }

  const handleGithubLogin = async () => {
    setError("")
    
    if (isGitHubConfigured()) {
      // Real GitHub OAuth - redirect flow
      setIsLoading(true)
      initiateGitHubLogin()
      // Page will redirect, no need to handle response here
    } else if (oauthConfig.features.enableMockAuth) {
      // Fallback to mock
      setIsLoading(true)
      try {
        const result = await mockGitHubLogin()
        if (result.success && result.user) {
          login(convertOAuthUser(result.user))
          navigate("/app")
        } else {
          setError(result.error || "GitHub login failed")
        }
      } catch {
        setError("An unexpected error occurred. Please try again.")
      } finally {
        setIsLoading(false)
      }
    } else {
      setError("GitHub OAuth not configured")
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
        {/* Error Message */}
        {error && (
          <div className="p-3 rounded-lg bg-neon-red/10 border border-neon-red/30 text-neon-red text-sm">
            {error}
          </div>
        )}

        {/* OAuth Status (dev info) */}
        {oauthStatus && import.meta.env.DEV && (
          <div className="p-2 rounded-lg bg-neon-cyan/10 border border-neon-cyan/30 text-neon-cyan text-xs text-center">
            {oauthStatus}
          </div>
        )}

        {/* Email Field */}
        <div className="space-y-2">
          <label htmlFor="email" className="text-sm font-medium text-text-secondary">
            Email
          </label>
          <EmailInput
            id="email"
            placeholder="Enter your email"
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

        {/* Social Login */}
        <SocialLoginButtons
          onGoogleLogin={handleGoogleLogin}
          onGithubLogin={handleGithubLogin}
          isLoading={isLoading}
        />

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
