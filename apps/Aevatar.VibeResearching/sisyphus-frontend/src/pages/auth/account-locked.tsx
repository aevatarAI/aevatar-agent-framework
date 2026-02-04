import { useState, useEffect } from "react"
import { Link } from "react-router-dom"
import { Lock, ArrowLeft, HelpCircle } from "lucide-react"
import { AuthLayout } from "@/components/auth"
import { Button } from "@/components/ui/button"

// ============================================================
//  Account Locked Page - Too Many Failed Attempts
// ============================================================

export default function AccountLockedPage() {
  // Countdown timer (15 minutes = 900 seconds)
  const [timeRemaining, setTimeRemaining] = useState(900)

  useEffect(() => {
    if (timeRemaining <= 0) return

    const timer = setInterval(() => {
      setTimeRemaining(prev => Math.max(0, prev - 1))
    }, 1000)

    return () => clearInterval(timer)
  }, [timeRemaining])

  // Format time as MM:SS
  const formatTime = (seconds: number) => {
    const mins = Math.floor(seconds / 60)
    const secs = seconds % 60
    return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`
  }

  const isUnlocked = timeRemaining === 0

  return (
    <AuthLayout
      title="Account Locked"
      subtitle="Too many failed login attempts"
    >
      <div className="text-center space-y-6">
        {/* Lock Icon */}
        <div className="mx-auto w-20 h-20 rounded-full bg-neon-red/10 border border-neon-red/30 flex items-center justify-center">
          <Lock className="w-10 h-10 text-neon-red" />
        </div>

        {/* Message */}
        <div className="space-y-3">
          <p className="text-sm text-text-muted text-center max-w-xs mx-auto">
            Your account has been temporarily locked due to multiple failed login attempts.
          </p>
          
          {!isUnlocked ? (
            <div className="rounded-xl bg-bg-base/50 border border-border-subtle p-5 space-y-3">
              <p className="text-xs text-text-muted">
                Time remaining
              </p>
              <div className="font-display text-5xl font-bold text-neon-gold tracking-wider">
                {formatTime(timeRemaining)}
              </div>
              <p className="text-xs text-text-dimmed">
                minutes
              </p>
            </div>
          ) : (
            <div className="p-3 rounded-lg bg-neon-green/10 border border-neon-green/30 text-neon-green">
              Your account is now unlocked. You can try signing in again.
            </div>
          )}
        </div>

        {/* Actions */}
        <div className="space-y-3 pt-4">
          {isUnlocked ? (
            <Link to="/login">
              <Button className="w-full">
                Return to Sign In
              </Button>
            </Link>
          ) : (
            <>
              <Button
                variant="secondary"
                className="w-full"
                onClick={() => window.open("mailto:support@sisyphus.ai")}
              >
                <HelpCircle className="w-4 h-4" />
                Contact Support
              </Button>

              <Link
                to="/login"
                className="inline-flex items-center gap-2 text-sm text-text-muted hover:text-neon-cyan transition-colors"
              >
                <ArrowLeft className="w-4 h-4" />
                Return to Sign In
              </Link>
            </>
          )}
        </div>

        {/* Security Note */}
        <div className="pt-6 border-t border-border-subtle">
          <p className="text-xs text-text-dimmed max-w-sm mx-auto">
            For security reasons, we lock accounts after multiple failed login attempts. 
            If you've forgotten your password, you can{" "}
            <Link to="/forgot-password" className="text-neon-cyan hover:underline">
              reset it here
            </Link>.
          </p>
        </div>
      </div>
    </AuthLayout>
  )
}
