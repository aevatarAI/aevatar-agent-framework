import * as React from "react"
import { cn } from "@/lib/utils"

// ============================================================
//  Dialog Component - Cyberpunk Style
//  Simple modal dialog without external dependencies
// ============================================================

// Context for close function
const DialogContext = React.createContext<{ onClose: () => void } | null>(null)

interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  children: React.ReactNode
}

export function Dialog({ open, onOpenChange, children }: DialogProps) {
  const onClose = React.useCallback(() => onOpenChange(false), [onOpenChange])

  // Handle ESC key
  React.useEffect(() => {
    const handleEsc = (e: KeyboardEvent) => {
      if (e.key === "Escape" && open) {
        onClose()
      }
    }
    document.addEventListener("keydown", handleEsc)
    return () => document.removeEventListener("keydown", handleEsc)
  }, [open, onClose])

  // Prevent body scroll when modal is open
  React.useEffect(() => {
    if (open) {
      document.body.style.overflow = "hidden"
    } else {
      document.body.style.overflow = ""
    }
    return () => {
      document.body.style.overflow = ""
    }
  }, [open])

  if (!open) return null

  return (
    <DialogContext.Provider value={{ onClose }}>
      <div className="fixed inset-0 z-50">
        {/* Backdrop */}
        <div 
          className="fixed inset-0 bg-bg-void/80 backdrop-blur-sm animate-fade-in"
          onClick={onClose}
        />
        {/* Content wrapper */}
        <div className="fixed inset-0 flex items-center justify-center p-4 pointer-events-none">
          <div className="pointer-events-auto max-h-[90vh] w-full max-w-2xl animate-scale-in">
            {children}
          </div>
        </div>
      </div>
    </DialogContext.Provider>
  )
}

interface DialogContentProps {
  children: React.ReactNode
  className?: string
}

export function DialogContent({ children, className }: DialogContentProps) {
  return (
    <div className={cn(
      "relative flex flex-col max-h-[85vh] rounded-xl",
      "bg-bg-surface border border-border-default",
      "shadow-2xl shadow-neon-cyan/10",
      className
    )}>
      {children}
    </div>
  )
}

interface DialogHeaderProps {
  children: React.ReactNode
  className?: string
}

export function DialogHeader({ children, className }: DialogHeaderProps) {
  return (
    <div className={cn(
      "flex items-start justify-between p-4 border-b border-border-subtle",
      className
    )}>
      {children}
    </div>
  )
}

interface DialogTitleProps {
  children: React.ReactNode
  className?: string
}

export function DialogTitle({ children, className }: DialogTitleProps) {
  return (
    <h2 className={cn(
      "text-base font-display font-semibold text-text-primary tracking-wide",
      className
    )}>
      {children}
    </h2>
  )
}

interface DialogDescriptionProps {
  children: React.ReactNode
  className?: string
}

export function DialogDescription({ children, className }: DialogDescriptionProps) {
  return (
    <p className={cn(
      "text-xs text-text-muted font-mono mt-1",
      className
    )}>
      {children}
    </p>
  )
}

interface DialogCloseButtonProps {
  onClick?: () => void
  className?: string
}

export function DialogCloseButton({ onClick, className }: DialogCloseButtonProps) {
  const context = React.useContext(DialogContext)
  
  const handleClick = () => {
    if (onClick) {
      onClick()
    } else if (context?.onClose) {
      context.onClose()
    }
  }

  return (
    <button
      onClick={handleClick}
      aria-label="Close dialog"
      className={cn(
        "absolute top-3 right-3 p-1.5 rounded-lg",
        "text-text-muted hover:text-text-primary",
        "hover:bg-bg-elevated transition-colors",
        className
      )}
    >
      <svg className="size-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
      </svg>
    </button>
  )
}
