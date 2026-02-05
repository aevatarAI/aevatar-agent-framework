// ============================================================================
//  Codex Connection Card - ChatGPT OAuth status & connect/disconnect
//  Supports both Localhost (redirect) and Device Code (RFC 8628) auth modes.
// ============================================================================

import React, { useState, useCallback, useEffect, useRef } from 'react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { ExternalLink, Unlink, Loader2, CheckCircle2, XCircle, Copy, Check } from 'lucide-react'
import { useToast } from '@/components/ui/toast'
import {
  initiateCodexOAuth,
  disconnectCodex,
  getCodexAuthMode,
  initiateDeviceCode,
  pollDeviceCode,
} from '@/lib/axiom-client/user-provider'
import { useUserProviderStore } from '@/store/user-provider-store'

interface CodexConnectionCardProps {
  className?: string
}

type AuthMode = 'localhost' | 'devicecode'

interface DeviceCodeState {
  deviceAuthId: string
  userCode: string
  verificationUri: string
  interval: number
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
  const [authMode, setAuthMode] = useState<AuthMode | null>(null)
  const [deviceCode, setDeviceCode] = useState<DeviceCodeState | null>(null)
  const [copied, setCopied] = useState(false)
  const pollTimerRef = useRef<ReturnType<typeof setInterval> | null>(null)

  // Fetch auth mode on mount
  useEffect(() => {
    getCodexAuthMode()
      .then((res) => setAuthMode(res.authMode))
      .catch(() => setAuthMode('localhost')) // fallback
  }, [])

  // Clean up poll timer
  useEffect(() => {
    return () => {
      if (pollTimerRef.current) clearInterval(pollTimerRef.current)
    }
  }, [])

  const handleConnectLocalhost = useCallback(async () => {
    setConnecting(true)
    try {
      const redirectUri = 'http://localhost:1455/auth/callback'
      const res = await initiateCodexOAuth({ redirectUri })
      if (res?.authUrl) {
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

  const handleConnectDeviceCode = useCallback(async () => {
    setConnecting(true)
    try {
      const res = await initiateDeviceCode()
      setDeviceCode({
        deviceAuthId: res.deviceAuthId,
        userCode: res.userCode,
        verificationUri: res.verificationUri,
        interval: Math.max(res.interval, 5),
      })

      // Start polling
      const intervalMs = Math.max(res.interval, 5) * 1000
      pollTimerRef.current = setInterval(async () => {
        try {
          const poll = await pollDeviceCode({
            deviceAuthId: res.deviceAuthId,
            userCode: res.userCode,
          })
          if (poll.status === 'connected') {
            if (pollTimerRef.current) clearInterval(pollTimerRef.current)
            pollTimerRef.current = null
            setDeviceCode(null)
            setConnecting(false)
            setCodexStatus({
              connected: true,
              email: poll.email ?? undefined,
              providerId: poll.providerId ?? undefined,
            })
            await fetchProviders()
            toast.success('Codex connected', `Signed in as ${poll.email ?? 'ChatGPT user'}`)
          } else if (poll.status === 'expired') {
            if (pollTimerRef.current) clearInterval(pollTimerRef.current)
            pollTimerRef.current = null
            setDeviceCode(null)
            setConnecting(false)
            toast.error('Code expired', 'The device code has expired. Please try again.')
          }
          // "pending" — keep polling
        } catch {
          // Transient error — keep polling
        }
      }, intervalMs)
    } catch (e) {
      const err = e as Error
      toast.error('Device code request failed', err.message)
      setConnecting(false)
    }
  }, [toast, setCodexStatus, fetchProviders])

  const handleConnect = useCallback(() => {
    if (authMode === 'devicecode') {
      handleConnectDeviceCode()
    } else {
      handleConnectLocalhost()
    }
  }, [authMode, handleConnectDeviceCode, handleConnectLocalhost])

  const handleCancelDeviceCode = useCallback(() => {
    if (pollTimerRef.current) clearInterval(pollTimerRef.current)
    pollTimerRef.current = null
    setDeviceCode(null)
    setConnecting(false)
  }, [])

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

  const handleCopyCode = useCallback(() => {
    if (!deviceCode) return
    navigator.clipboard.writeText(deviceCode.userCode)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }, [deviceCode])

  const connected = codexStatus?.connected ?? false

  // Device code pending UI
  if (deviceCode) {
    return (
      <div
        className={cn(
          'card p-5 transition-all duration-200 border-neon-cyan/30 bg-neon-cyan/5',
          className
        )}
      >
        <div className="flex flex-col gap-3">
          <div className="flex items-center gap-2">
            <CodexLogo />
            <h3 className="text-sm font-display font-semibold text-text-primary">
              Authorize ChatGPT
            </h3>
          </div>

          <p className="text-[11px] text-text-muted">
            Go to the link below and enter this code to connect your ChatGPT account:
          </p>

          {/* User code display */}
          <div className="flex items-center gap-2">
            <code className="flex-1 text-center text-xl font-mono font-bold text-neon-cyan bg-surface-elevated border border-neon-cyan/20 rounded-md py-2 tracking-[0.3em]">
              {deviceCode.userCode}
            </code>
            <Button variant="ghost" size="sm" onClick={handleCopyCode} className="px-2">
              {copied ? (
                <Check className="w-4 h-4 text-neon-green" />
              ) : (
                <Copy className="w-4 h-4" />
              )}
            </Button>
          </div>

          {/* Verification link */}
          <a
            href={deviceCode.verificationUri}
            target="_blank"
            rel="noopener noreferrer"
            className="text-[11px] font-mono text-neon-cyan hover:text-neon-cyan/80 underline flex items-center gap-1"
          >
            <ExternalLink className="w-3 h-3" />
            {deviceCode.verificationUri}
          </a>

          {/* Polling indicator */}
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2 text-[10px] text-text-dimmed">
              <Loader2 className="w-3 h-3 animate-spin" />
              Waiting for authorization...
            </div>
            <Button variant="ghost" size="sm" onClick={handleCancelDeviceCode}>
              Cancel
            </Button>
          </div>
        </div>
      </div>
    )
  }

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
              disabled={connecting || authMode === null}
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
