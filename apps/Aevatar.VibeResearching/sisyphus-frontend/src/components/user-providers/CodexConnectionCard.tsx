// ============================================================================
//  Codex Connection Card - ChatGPT OAuth status & connect/disconnect
// ============================================================================

import React, { useState, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { ExternalLink, Unlink, Loader2, CheckCircle2, XCircle } from 'lucide-react'
import { useToast } from '@/components/ui/toast'
import {
  initiateCodexOAuth,
  disconnectCodex,
} from '@/lib/axiom-client/user-provider'
import { useUserProviderStore } from '@/store/user-provider-store'

interface CodexConnectionCardProps {
  className?: string
}

/** Card showing Codex (ChatGPT) OAuth connection status with connect/disconnect actions. */
export const CodexConnectionCard: React.FC<CodexConnectionCardProps> = ({ className }) => {
  const codexStatus = useUserProviderStore((s) => s.codexStatus)
  const codexLoading = useUserProviderStore((s) => s.codexLoading)
  const setCodexStatus = useUserProviderStore((s) => s.setCodexStatus)
  const fetchProviders = useUserProviderStore((s) => s.fetchProviders)
  const toast = useToast()

  const [connecting, setConnecting] = useState(false)
  const [disconnecting, setDisconnecting] = useState(false)

  const handleConnect = useCallback(async () => {
    setConnecting(true)
    try {
      // Fixed redirect URI matching OpenAI's registered callback for Codex CLI client
      const redirectUri = 'http://localhost:1455/auth/callback'
      const res = await initiateCodexOAuth({ redirectUri })
      if (res?.authUrl) {
        // Redirect to OpenAI authorization — backend callback listener on port 1455
        // handles the response and redirects back to frontend
        window.location.href = res.authUrl
      } else {
        toast.error('Failed to initiate OAuth', 'No authorization URL returned')
      }
    } catch (e) {
      const err = e as Error
      toast.error('OAuth initiation failed', err.message)
    }
    setConnecting(false)
  }, [toast])

  const handleDisconnect = useCallback(async () => {
    setDisconnecting(true)
    try {
      await disconnectCodex()
      setCodexStatus({ connected: false })
      await fetchProviders()
      toast.success('Codex disconnected', 'Your ChatGPT connection has been removed.')
    } catch (e) {
      const err = e as Error
      toast.error('Disconnect failed', err.message)
    }
    setDisconnecting(false)
  }, [setCodexStatus, fetchProviders, toast])

  const connected = codexStatus?.connected ?? false

  return (
    <div
      className={cn(
        'card p-5 transition-all duration-200',
        connected
          ? 'border-neon-green/30 bg-neon-green/5'
          : 'border-border-subtle',
        className
      )}
    >
      <div className="flex items-start justify-between gap-4">
        {/* Left: Info */}
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 mb-1">
            <CodexLogo />
            <h3 className="text-sm font-display font-semibold text-text-primary">
              ChatGPT (Codex)
            </h3>
            {connected ? (
              <span className="flex items-center gap-1 text-[9px] font-mono text-neon-green bg-neon-green/10 px-1.5 py-0.5 rounded">
                <CheckCircle2 className="w-2.5 h-2.5" />
                Connected
              </span>
            ) : (
              <span className="flex items-center gap-1 text-[9px] font-mono text-text-dimmed bg-surface-elevated px-1.5 py-0.5 rounded">
                <XCircle className="w-2.5 h-2.5" />
                Not connected
              </span>
            )}
          </div>
          <p className="text-[11px] text-text-muted mb-2">
            {connected
              ? 'Use your ChatGPT subscription to access OpenAI models without a separate API key.'
              : 'Connect your ChatGPT subscription via OAuth to use OpenAI models.'}
          </p>
          {connected && codexStatus?.email && (
            <div className="text-[10px] font-mono text-text-dimmed">
              {codexStatus.email}
              {codexStatus.connectedAt && (
                <span className="ml-2">
                  Connected {new Date(codexStatus.connectedAt).toLocaleDateString()}
                </span>
              )}
            </div>
          )}
        </div>

        {/* Right: Action */}
        <div className="flex-shrink-0">
          {codexLoading ? (
            <Loader2 className="w-5 h-5 animate-spin text-text-muted" />
          ) : connected ? (
            <Button
              variant="destructiveOutline"
              size="sm"
              onClick={handleDisconnect}
              disabled={disconnecting}
            >
              {disconnecting ? (
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
              ) : (
                <Unlink className="w-3.5 h-3.5" />
              )}
              Disconnect
            </Button>
          ) : (
            <Button
              variant="outline"
              size="sm"
              onClick={handleConnect}
              disabled={connecting}
            >
              {connecting ? (
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
              ) : (
                <ExternalLink className="w-3.5 h-3.5" />
              )}
              Connect ChatGPT
            </Button>
          )}
        </div>
      </div>
    </div>
  )
}

// === Codex Logo (simple SVG placeholder) ===

const CodexLogo: React.FC = () => (
  <div className="w-6 h-6 rounded-md bg-neon-green/10 border border-neon-green/30 flex items-center justify-center">
    <span className="text-[10px] font-bold text-neon-green">CX</span>
  </div>
)
