// ============================================================
//  Toast Notification System - Radix UI + Pencil Design Spec
// ============================================================

import React, { createContext, useContext, useState, useCallback } from 'react'
import * as ToastPrimitive from '@radix-ui/react-toast'
import { X, CircleX, CircleAlert, Info, CheckCircle2 } from 'lucide-react'
import { cn } from '@/lib/utils'

// ============================================================
//  Types
// ============================================================

type ToastType = 'success' | 'error' | 'info' | 'warning'

interface Toast {
  id: string
  type: ToastType
  title: string
  description?: string
}

interface ToastContextValue {
  toast: (type: ToastType, title: string, description?: string) => void
  success: (title: string, description?: string) => void
  error: (title: string, description?: string) => void
  info: (title: string, description?: string) => void
  warning: (title: string, description?: string) => void
}

// ============================================================
//  Context
// ============================================================

const ToastContext = createContext<ToastContextValue | null>(null)

export function useToast() {
  const context = useContext(ToastContext)
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider')
  }
  return context
}

// ============================================================
//  Toast Styles - Based on Pencil Design Spec
// ============================================================

const toastStyles: Record<ToastType, {
  bg: string
  border: string
  title: string
  icon: React.ReactNode
}> = {
  error: {
    bg: 'bg-[#f8717112]',
    border: 'border-[#f8717130]',
    title: 'text-[#f87171]',
    icon: <CircleX className="w-[18px] h-[18px] text-[#f87171] flex-shrink-0" />,
  },
  warning: {
    bg: 'bg-[#fcd34d12]',
    border: 'border-[#fcd34d30]',
    title: 'text-neon-gold',
    icon: <CircleAlert className="w-[18px] h-[18px] text-neon-gold flex-shrink-0" />,
  },
  success: {
    bg: 'bg-[#4ade8012]',
    border: 'border-[#4ade8030]',
    title: 'text-[#4ade80]',
    icon: <CheckCircle2 className="w-[18px] h-[18px] text-[#4ade80] flex-shrink-0" />,
  },
  info: {
    bg: 'bg-[#22d3ee12]',
    border: 'border-[#22d3ee30]',
    title: 'text-neon-cyan',
    icon: <Info className="w-[18px] h-[18px] text-neon-cyan flex-shrink-0" />,
  },
}

// ============================================================
//  Provider
// ============================================================

const TOAST_DURATION = 5000

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  const addToast = useCallback((type: ToastType, title: string, description?: string) => {
    const id = `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`
    setToasts((prev) => [...prev, { id, type, title, description }])
  }, [])

  const toast = useCallback((type: ToastType, title: string, description?: string) => {
    addToast(type, title, description)
  }, [addToast])

  const success = useCallback((title: string, description?: string) => {
    addToast('success', title, description)
  }, [addToast])

  const error = useCallback((title: string, description?: string) => {
    addToast('error', title, description)
  }, [addToast])

  const info = useCallback((title: string, description?: string) => {
    addToast('info', title, description)
  }, [addToast])

  const warning = useCallback((title: string, description?: string) => {
    addToast('warning', title, description)
  }, [addToast])

  return (
    <ToastContext.Provider value={{ toast, success, error, info, warning }}>
      <ToastPrimitive.Provider swipeDirection="right" duration={TOAST_DURATION}>
        {children}
        
        {/* Render each toast */}
        {toasts.map((t) => (
          <ToastItem
            key={t.id}
            toast={t}
            onOpenChange={(open) => {
              if (!open) removeToast(t.id)
            }}
          />
        ))}
        
        {/* Viewport - Fixed position container */}
        <ToastPrimitive.Viewport
          className={cn(
            'fixed top-4 right-4 z-[9999]',
            'flex flex-col gap-3',
            'w-[400px] max-w-[calc(100vw-32px)]',
            'outline-none'
          )}
        />
      </ToastPrimitive.Provider>
    </ToastContext.Provider>
  )
}

// ============================================================
//  Toast Item Component
// ============================================================

interface ToastItemProps {
  toast: Toast
  onOpenChange: (open: boolean) => void
}

function ToastItem({ toast, onOpenChange }: ToastItemProps) {
  const style = toastStyles[toast.type]

  return (
    <ToastPrimitive.Root
      open={true}
      onOpenChange={onOpenChange}
      className={cn(
        'flex items-start gap-[10px] p-3 rounded-lg border',
        'font-["Space_Grotesk",sans-serif]',
        'shadow-lg backdrop-blur-sm',
        // Radix animation classes
        'data-[state=open]:animate-in data-[state=open]:slide-in-from-right-full',
        'data-[state=closed]:animate-out data-[state=closed]:slide-out-to-right-full',
        'data-[swipe=move]:translate-x-[var(--radix-toast-swipe-move-x)]',
        'data-[swipe=cancel]:translate-x-0 data-[swipe=cancel]:transition-transform',
        'data-[swipe=end]:animate-out data-[swipe=end]:slide-out-to-right-full',
        style.bg,
        style.border
      )}
    >
      {/* Icon */}
      {style.icon}
      
      {/* Content */}
      <div className="flex-1 flex flex-col gap-0.5 min-w-0">
        <ToastPrimitive.Title className={cn('text-[13px] font-medium leading-tight', style.title)}>
          {toast.title}
        </ToastPrimitive.Title>
        {toast.description && (
          <ToastPrimitive.Description className="text-[11px] font-normal text-text-muted leading-snug">
            {toast.description}
          </ToastPrimitive.Description>
        )}
      </div>
      
      {/* Close Button */}
      <ToastPrimitive.Close
        className={cn(
          'p-0.5 rounded hover:bg-white/10 transition-colors flex-shrink-0',
          style.title
        )}
        aria-label="Dismiss"
      >
        <X className="w-4 h-4" />
      </ToastPrimitive.Close>
    </ToastPrimitive.Root>
  )
}

// ============================================================
//  Re-export Radix primitives for advanced usage
// ============================================================

export const ToastViewport = ToastPrimitive.Viewport
export const ToastRoot = ToastPrimitive.Root
export const ToastTitle = ToastPrimitive.Title
export const ToastDescription = ToastPrimitive.Description
export const ToastClose = ToastPrimitive.Close
export const ToastAction = ToastPrimitive.Action
