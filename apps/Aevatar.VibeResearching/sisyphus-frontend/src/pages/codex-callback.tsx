// ============================================================================
//  Codex OAuth Callback Page
//  Displays the result after the backend (port 1455) handles the OAuth flow.
//  Query params: ?status=connected&email=... or ?status=error&message=...
// ============================================================================

import React, { useEffect, useRef } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { CheckCircle2, XCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useUserProviderStore } from '@/store/user-provider-store'

/** Landing page after Codex OAuth redirect. Reads result from query params. */
const CodexCallbackPage: React.FC = () => {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const fetchProviders = useUserProviderStore((s) => s.fetchProviders)
  const fetchCodexStatus = useUserProviderStore((s) => s.fetchCodexStatus)
  const refreshed = useRef(false)

  const status = searchParams.get('status') ?? 'error'
  const email = searchParams.get('email')
  const message = searchParams.get('message') ?? 'Unknown error occurred.'
  const connected = status === 'connected'

  // Refresh provider data on successful connection
  useEffect(() => {
    if (connected && !refreshed.current) {
      refreshed.current = true
      Promise.all([fetchProviders(), fetchCodexStatus()])
    }
  }, [connected, fetchProviders, fetchCodexStatus])

  return (
    <div className="min-h-screen flex items-center justify-center bg-bg-base p-4">
      <div className="card max-w-md w-full p-8 text-center">
        {connected ? (
          <>
            <CheckCircle2 className="w-12 h-12 mx-auto text-neon-green mb-4" />
            <h2 className="text-lg font-display font-semibold text-neon-green mb-2">
              Connected
            </h2>
            <p className="text-sm text-text-muted mb-1">
              ChatGPT connected successfully!
            </p>
            {email && (
              <p className="text-[11px] font-mono text-text-dimmed mb-6">{email}</p>
            )}
            <Button onClick={() => navigate('/')} className="mt-4">
              Return to App
            </Button>
          </>
        ) : (
          <>
            <XCircle className="w-12 h-12 mx-auto text-neon-red mb-4" />
            <h2 className="text-lg font-display font-semibold text-neon-red mb-2">
              Connection Failed
            </h2>
            <p className="text-sm text-text-muted mb-6">{message}</p>
            <div className="flex gap-3 justify-center">
              <Button variant="ghost" onClick={() => navigate('/')}>
                Back to App
              </Button>
              <Button onClick={() => navigate('/account/providers')}>
                Try Again
              </Button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}

export default CodexCallbackPage
