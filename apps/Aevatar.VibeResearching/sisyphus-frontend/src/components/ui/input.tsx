import * as React from "react"
import { Eye, EyeOff, Search, Mail, Lock } from "lucide-react"
import type { LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"

// ============================================================
//  Input Component - Cyberpunk Style with Icon Support
// ============================================================

export interface InputProps
  extends React.InputHTMLAttributes<HTMLInputElement> {
  error?: string
  icon?: LucideIcon
}

const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, type, error, icon: Icon, ...props }, ref) => {
    return (
      <div className="relative w-full">
        <div
          className={cn(
            "flex items-center h-12 w-full rounded-lg border bg-surface px-4 gap-3",
            "border-border-subtle hover:border-border-default",
            "focus-within:border-neon-cyan focus-within:ring-2 focus-within:ring-neon-cyan/20",
            "transition-all duration-200",
            error && "border-neon-red focus-within:border-neon-red focus-within:ring-neon-red/20",
            className
          )}
        >
          {Icon && (
            <Icon className="w-[18px] h-[18px] text-text-dimmed flex-shrink-0" />
          )}
          <input
            type={type}
            className={cn(
              "flex-1 bg-transparent text-sm",
              "text-text-primary placeholder:text-text-dimmed",
              "focus:outline-none",
              "disabled:cursor-not-allowed disabled:opacity-50"
            )}
            ref={ref}
            {...props}
          />
        </div>
        {error && (
          <p className="mt-1.5 text-xs text-neon-red">{error}</p>
        )}
      </div>
    )
  }
)
Input.displayName = "Input"

// Password Input with Toggle
export interface PasswordInputProps
  extends Omit<InputProps, "type" | "icon"> {
  showIcon?: boolean
}

const PasswordInput = React.forwardRef<HTMLInputElement, PasswordInputProps>(
  ({ className, showIcon = true, ...props }, ref) => {
    const [showPassword, setShowPassword] = React.useState(false)

    return (
      <div className="relative w-full">
        <div
          className={cn(
            "flex items-center h-12 w-full rounded-lg border bg-surface px-4 gap-3",
            "border-border-subtle hover:border-border-default",
            "focus-within:border-neon-cyan focus-within:ring-2 focus-within:ring-neon-cyan/20",
            "transition-all duration-200",
            className
          )}
        >
          {showIcon && (
            <Lock className="w-[18px] h-[18px] text-text-dimmed flex-shrink-0" />
          )}
          <input
            type={showPassword ? "text" : "password"}
            className={cn(
              "flex-1 bg-transparent text-sm",
              "text-text-primary placeholder:text-text-dimmed",
              "focus:outline-none",
              "disabled:cursor-not-allowed disabled:opacity-50"
            )}
            ref={ref}
            {...props}
          />
          <button
            type="button"
            onClick={() => setShowPassword(!showPassword)}
            className="text-text-muted hover:text-text-primary transition-colors flex-shrink-0"
            tabIndex={-1}
          >
            {showPassword ? (
              <EyeOff className="h-4 w-4" />
            ) : (
              <Eye className="h-4 w-4" />
            )}
          </button>
        </div>
      </div>
    )
  }
)
PasswordInput.displayName = "PasswordInput"

// Search Input
export interface SearchInputProps extends Omit<InputProps, "type" | "icon"> {}

const SearchInput = React.forwardRef<HTMLInputElement, SearchInputProps>(
  ({ className, ...props }, ref) => {
    return (
      <div className="relative w-full">
        <div
          className={cn(
            "flex items-center h-9 w-full rounded-lg border bg-surface px-3 gap-2",
            "border-border-subtle hover:border-border-default",
            "focus-within:border-neon-cyan focus-within:ring-2 focus-within:ring-neon-cyan/20",
            "transition-all duration-200",
            className
          )}
        >
          <Search className="w-3.5 h-3.5 text-text-dimmed flex-shrink-0" />
          <input
            type="search"
            className={cn(
              "flex-1 bg-transparent text-[13px]",
              "text-text-primary placeholder:text-text-dimmed",
              "focus:outline-none",
              "disabled:cursor-not-allowed disabled:opacity-50"
            )}
            ref={ref}
            {...props}
          />
        </div>
      </div>
    )
  }
)
SearchInput.displayName = "SearchInput"

// Email Input (convenience component)
export interface EmailInputProps extends Omit<InputProps, "type" | "icon"> {}

const EmailInput = React.forwardRef<HTMLInputElement, EmailInputProps>(
  (props, ref) => {
    return <Input type="email" icon={Mail} ref={ref} {...props} />
  }
)
EmailInput.displayName = "EmailInput"

export { Input, PasswordInput, SearchInput, EmailInput }
