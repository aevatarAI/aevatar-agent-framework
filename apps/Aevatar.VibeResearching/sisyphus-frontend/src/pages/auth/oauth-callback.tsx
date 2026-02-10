import { useEffect, useRef, useState } from "react"
import { useNavigate, useSearchParams, useLocation } from "react-router-dom"
import { Loader2, AlertCircle, CheckCircle } from "lucide-react"
import { AuthLayout } from "@/components/auth"
import { Button } from "@/components/ui/button"
import { useAuthStore } from "@/store/auth-store"
import { handleGitHubCallback, handleGoogleCallback } from "@/lib/oauth"

// ============================================================
//  OAuth Callback Page - Handles GitHub & Google redirects
// ============================================================

type OAuthProvider = "github" | "google" | "unknown"

function detectProvider(pathname: string): OAuthProvider {
  if (pathname.includes("/callback/github")) return "github"
  if (pathname.includes("/callback/google")) return "google"
  return "unknown"
}

export default function OAuthCallbackPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const { login } = useAuthStore()
  
  const [status, setStatus] = useState<"loading" | "success" | "error">("loading")
  const [message, setMessage] = useState("Processing authentication...")
  const [provider, setProvider] = useState<OAuthProvider>("unknown")
  const calledRef = useRef(false)

  useEffect(() => {
    if (calledRef.current) return
    calledRef.current = true
    handleCallback()
  }, [])

  const handleCallback = async () => {
    const detectedProvider = detectProvider(location.pathname)
    setProvider(detectedProvider)

    const code = searchParams.get("code")
    const state = searchParams.get("state")
    const error = searchParams.get("error")
    const errorDescription = searchParams.get("error_description")

    // Handle OAuth error from provider
    if (error) {
      setStatus("error")
      setMessage(errorDescription || `OAuth error: ${error}`)
      return
    }

    // Validate required parameters
    if (!code || !state) {
      setStatus("error")
      setMessage("Invalid callback parameters. Please try again.")
      return
    }

    if (detectedProvider === "unknown") {
      setStatus("error")
      setMessage("Unknown OAuth provider. Please try again.")
      return
    }

    try {
      setMessage(`Completing ${detectedProvider === "github" ? "GitHub" : "Google"} authentication...`)
      
      // Call the appropriate handler based on provider
      const result = detectedProvider === "github"
        ? await handleGitHubCallback(code, state)
        : await handleGoogleCallback(code, state)

      if (result.success && result.user) {
        setStatus("success")
        setMessage("Authentication successful! Redirecting...")

        // Use the real ABP Identity user returned by backend
        login(result.user)
        
        // Redirect to app after short delay
        setTimeout(() => navigate("/app"), 1500)
      } else {
        setStatus("error")
        setMessage(result.error || "Authentication failed. Please try again.")
      }
    } catch (err) {
      console.error("[OAuth Callback] Error:", err)
      setStatus("error")
      setMessage("An unexpected error occurred. Please try again.")
    }
  }

  const providerName = provider === "github" ? "GitHub" : provider === "google" ? "Google" : "OAuth"

  return (
    <AuthLayout
      title={
        status === "loading" ? `Authenticating with ${providerName}...` :
        status === "success" ? "Success!" :
        "Authentication Failed"
      }
      subtitle={message}
    >
      <div className="flex flex-col items-center gap-6 py-8">
        {/* Status Icon */}
        {status === "loading" && (
          <div className="w-16 h-16 rounded-full bg-neon-cyan/20 flex items-center justify-center">
            <Loader2 className="w-8 h-8 text-neon-cyan animate-spin" />
          </div>
        )}
        {status === "success" && (
          <div className="w-16 h-16 rounded-full bg-neon-green/20 flex items-center justify-center">
            <CheckCircle className="w-8 h-8 text-neon-green" />
          </div>
        )}
        {status === "error" && (
          <div className="w-16 h-16 rounded-full bg-neon-red/20 flex items-center justify-center">
            <AlertCircle className="w-8 h-8 text-neon-red" />
          </div>
        )}

        {/* Error Actions */}
        {status === "error" && (
          <div className="flex gap-3">
            <Button variant="outline" onClick={() => navigate("/login")}>
              Back to Login
            </Button>
            <Button onClick={handleCallback}>
              Try Again
            </Button>
          </div>
        )}
      </div>
    </AuthLayout>
  )
}
