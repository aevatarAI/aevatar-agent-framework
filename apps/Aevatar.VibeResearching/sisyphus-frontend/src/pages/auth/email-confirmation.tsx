import { useState } from "react"
import { Link, useLocation } from "react-router-dom"
import { Mail, RefreshCw } from "lucide-react"
import { AuthLayout } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { mockResendVerification } from "@/lib/mock/auth"

// ============================================================
//  Email Confirmation Page - Verify Email Address
// ============================================================

export default function EmailConfirmationPage() {
  const location = useLocation()
  const email = location.state?.email || "your@email.com"
  
  const [isResending, setIsResending] = useState(false)
  const [resendSuccess, setResendSuccess] = useState(false)

  const handleResend = async () => {
    setIsResending(true)
    setResendSuccess(false)

    try {
      const result = await mockResendVerification(email)
      if (result.success) {
        setResendSuccess(true)
      }
    } catch (err) {
      // Silently fail
    } finally {
      setIsResending(false)
    }
  }

  return (
    <AuthLayout
      title="Verify Your Email"
      subtitle="We've sent you a verification link"
    >
      <div className="text-center space-y-6">
        {/* Icon */}
        <div className="mx-auto w-20 h-20 rounded-full bg-neon-cyan/10 border border-neon-cyan/30 flex items-center justify-center">
          <Mail className="w-10 h-10 text-neon-cyan" />
        </div>

        {/* Message */}
        <div className="space-y-3">
          <p className="text-text-secondary">
            We've sent a verification email to:
          </p>
          <p className="font-mono text-neon-cyan text-lg">{email}</p>
          <p className="text-sm text-text-muted max-w-sm mx-auto">
            Click the link in the email to verify your account. 
            If you don't see it, check your spam folder.
          </p>
        </div>

        {/* Resend Success */}
        {resendSuccess && (
          <div className="p-3 rounded-lg bg-neon-green/10 border border-neon-green/30 text-neon-green text-sm">
            Verification email sent successfully!
          </div>
        )}

        {/* Actions */}
        <div className="space-y-3 pt-4">
          <Button
            variant="secondary"
            className="w-full"
            onClick={handleResend}
            disabled={isResending}
          >
            <RefreshCw className={`w-4 h-4 ${isResending ? "animate-spin" : ""}`} />
            {isResending ? "Sending..." : "Resend Verification Email"}
          </Button>

          <p className="text-sm text-text-muted">
            Wrong email?{" "}
            <Link to="/register" className="text-neon-cyan hover:underline">
              Sign up again
            </Link>
          </p>
        </div>

        {/* Continue anyway (for demo) */}
        <div className="pt-6 border-t border-border-subtle">
          <Link to="/login">
            <Button variant="outline" className="w-full">
              Continue to Sign In
            </Button>
          </Link>
          <p className="mt-2 text-xs text-text-dimmed">
            (Demo mode: Email verification is simulated)
          </p>
        </div>
      </div>
    </AuthLayout>
  )
}
