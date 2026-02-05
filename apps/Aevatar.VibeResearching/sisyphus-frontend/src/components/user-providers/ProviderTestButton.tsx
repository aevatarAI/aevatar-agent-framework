// ============================================================================
//  Provider Test Button - Tests connectivity with loading & result display
// ============================================================================

import React, { useState, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Zap, CheckCircle2, XCircle, Loader2 } from 'lucide-react'
import { testUserProvider } from '@/lib/axiom-client/user-provider'
import type { ProviderTestResult } from '@/types/user-provider'

interface ProviderTestButtonProps {
  providerId: string
  className?: string
}

type TestState = 'idle' | 'loading' | 'success' | 'error'

/** Button that tests a provider's connectivity and shows the result inline. */
export const ProviderTestButton: React.FC<ProviderTestButtonProps> = ({
  providerId,
  className,
}) => {
  const [state, setState] = useState<TestState>('idle')
  const [result, setResult] = useState<ProviderTestResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const handleTest = useCallback(async () => {
    setState('loading')
    setResult(null)
    setError(null)
    try {
      const res = await testUserProvider(providerId)
      setResult(res)
      setState(res.ok ? 'success' : 'error')
      if (!res.ok) {
        setError(res.message || 'Connection test failed')
      }
    } catch (e) {
      const err = e as Error
      setError(err.message || 'Test failed')
      setState('error')
    }
  }, [providerId])

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <Button
        variant={state === 'success' ? 'successOutline' : state === 'error' ? 'destructiveOutline' : 'outline'}
        size="sm"
        onClick={handleTest}
        disabled={state === 'loading'}
      >
        {state === 'loading' ? (
          <Loader2 className="w-3.5 h-3.5 animate-spin" />
        ) : state === 'success' ? (
          <CheckCircle2 className="w-3.5 h-3.5" />
        ) : state === 'error' ? (
          <XCircle className="w-3.5 h-3.5" />
        ) : (
          <Zap className="w-3.5 h-3.5" />
        )}
        {state === 'loading' ? 'Testing...' : 'Test Connection'}
      </Button>

      {/* Result display */}
      {state === 'success' && result && (
        <div className="px-2 py-1.5 rounded bg-neon-green/5 border border-neon-green/20">
          <span className="text-[10px] font-mono text-neon-green">
            {result.message}{result.latencyMs > 0 ? ` (${result.latencyMs}ms)` : ''}
          </span>
        </div>
      )}
      {state === 'error' && error && (
        <div className="px-2 py-1.5 rounded bg-neon-red/5 border border-neon-red/20">
          <span className="text-[10px] font-mono text-neon-red">{error}</span>
        </div>
      )}
    </div>
  )
}
