import { Link } from "react-router-dom"
import { ShieldX, ArrowLeft, Home } from "lucide-react"
import { Button } from "@/components/ui/button"

// ============================================================
//  403 Access Denied Page
// ============================================================

export default function AccessDeniedPage() {
  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4">
      <div className="text-center max-w-md">
        {/* Icon */}
        <div className="mx-auto w-24 h-24 rounded-full bg-neon-red/10 border border-neon-red/30 flex items-center justify-center mb-6">
          <ShieldX className="w-12 h-12 text-neon-red" />
        </div>

        {/* Error Code */}
        <p className="text-6xl font-display font-bold text-neon-red mb-2">403</p>

        {/* Title */}
        <h1 className="text-2xl font-semibold text-text-primary mb-2">
          Access Denied
        </h1>

        {/* Description */}
        <p className="text-text-muted mb-8">
          You don't have permission to access this page. 
          Please contact your administrator if you believe this is an error.
        </p>

        {/* Actions */}
        <div className="flex flex-col sm:flex-row items-center justify-center gap-3">
          <Button
            variant="secondary"
            onClick={() => window.history.back()}
          >
            <ArrowLeft className="w-4 h-4" />
            Go Back
          </Button>
          <Link to="/app">
            <Button>
              <Home className="w-4 h-4" />
              Return Home
            </Button>
          </Link>
        </div>

        {/* Footer */}
        <p className="mt-12 text-xs text-text-dimmed">
          If you need access, please contact{" "}
          <a href="mailto:support@sisyphus.ai" className="text-neon-cyan hover:underline">
            support@sisyphus.ai
          </a>
        </p>
      </div>
    </div>
  )
}
