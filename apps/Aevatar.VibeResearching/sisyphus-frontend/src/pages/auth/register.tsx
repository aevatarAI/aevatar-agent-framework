import React, { useState } from "react"
import { Link, useNavigate } from "react-router-dom"
import { User, UserPlus } from "lucide-react"
import { AuthLayout, SocialLoginButtons } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { Input, EmailInput, PasswordInput } from "@/components/ui/input"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { mockRegister, mockSocialLogin } from "@/lib/mock/auth"
import { useAuthStore } from "@/store/auth-store"

// ============================================================
//  Register Page - Create New Account
// ============================================================

export default function RegisterPage() {
  const navigate = useNavigate()
  const { login } = useAuthStore()
  
  const [formData, setFormData] = useState({
    name: "",
    email: "",
    password: "",
    confirmPassword: "",
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

    // Validate passwords match
    if (formData.password !== formData.confirmPassword) {
      setError("Passwords do not match")
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
      // Split name into first/last
      const nameParts = formData.name.trim().split(/\s+/)
      const firstName = nameParts[0] || ""
      const lastName = nameParts.slice(1).join(" ") || ""

      const result = await mockRegister({
        userName: formData.email.split("@")[0],
        email: formData.email,
        password: formData.password,
        name: firstName,
        surname: lastName,
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

  const handleSocialLogin = async (provider: "google" | "github") => {
    setError("")
    setIsLoading(true)

    try {
      const result = await mockSocialLogin(provider)
      
      if (result.success && result.user) {
        login(result.user)
        navigate("/app")
      } else {
        setError(result.error || "Social login failed")
      }
    } catch (err) {
      setError("An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
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

        {/* Full Name Field */}
        <div className="space-y-2">
          <label htmlFor="name" className="text-sm font-medium text-text-secondary">
            Full Name
          </label>
          <Input
            id="name"
            type="text"
            icon={User}
            placeholder="Enter your full name"
            value={formData.name}
            onChange={(e) => updateField("name", e.target.value)}
            required
            disabled={isLoading}
          />
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

        {/* Social Login */}
        <SocialLoginButtons
          onGoogleLogin={() => handleSocialLogin("google")}
          onGithubLogin={() => handleSocialLogin("github")}
          isLoading={isLoading}
        />

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
