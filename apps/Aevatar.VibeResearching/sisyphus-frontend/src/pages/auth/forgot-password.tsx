import React, { useState } from "react"
import { Link } from "react-router-dom"
import { ArrowLeft, Mail } from "lucide-react"
import { AuthLayout } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { EmailInput } from "@/components/ui/input"
import { useToast } from "@/components/ui/toast"
import { abpForgotPassword } from "@/lib/abp"

// ============================================================
//  Forgot Password Page - Request Password Reset
// ============================================================

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState("")
  const [isLoading, setIsLoading] = useState(false)
  const [isSubmitted, setIsSubmitted] = useState(false)
  const { error: showError } = useToast()

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)

    try {
      const result = await abpForgotPassword(email)
      
      if (result.success) {
        setIsSubmitted(true)
      } else {
        showError(result.error || "Failed to send reset email")
      }
    } catch {
      showError("An unexpected error occurred. Please try again.")
    } finally {
      setIsLoading(false)
    }
  }

  if (isSubmitted) {
    return (
      <AuthLayout
        title="Check Your Email"
        subtitle="We've sent you a password reset link"
      >
        <div className="text-center space-y-6">
          {/* Success Icon */}
          <div className="mx-auto w-16 h-16 rounded-full bg-neon-green/10 border border-neon-green/30 flex items-center justify-center">
            <Mail className="w-8 h-8 text-neon-green" />
          </div>

          <div className="space-y-2">
            <p className="text-text-secondary">
              We've sent a password reset link to:
            </p>
            <p className="font-mono text-neon-cyan">{email}</p>
          </div>

          <p className="text-sm text-text-muted">
            Click the link in the email to reset your password. 
            If you don't see it, check your spam folder.
          </p>

          <div className="pt-4 space-y-3">
            <Button
              variant="secondary"
              className="w-full"
              onClick={() => setIsSubmitted(false)}
            >
              Try a different email
            </Button>

            <Link
              to="/login"
              className="inline-flex items-center gap-2 text-sm text-text-muted hover:text-neon-cyan transition-colors"
            >
              <ArrowLeft className="w-4 h-4" />
              Back to sign in
            </Link>
          </div>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Forgot Password"
      subtitle="Enter your email to receive a reset link"
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {/* Email Field */}
        <div className="space-y-2">
          <label htmlFor="email" className="text-sm font-medium text-text-secondary">
            Email Address
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

        {/* Submit Button */}
        <Button
          type="submit"
          className="w-full"
          disabled={isLoading}
        >
          {isLoading ? "Sending..." : "Send Reset Link"}
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
