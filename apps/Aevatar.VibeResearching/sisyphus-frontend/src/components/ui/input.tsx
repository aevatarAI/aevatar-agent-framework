import * as React from "react"
import { cn } from "@/lib/utils"

// ============================================================
//  Input Component - Nebula Warm Theme
//  High contrast focus states for better visibility
// ============================================================

const Input = React.forwardRef<HTMLInputElement, React.ComponentProps<"input">>(
  ({ className, type, ...props }, ref) => {
    return (
      <input
        type={type}
        className={cn(
          "flex h-10 w-full rounded-lg border border-border bg-background px-3 py-2 text-sm text-text-primary",
          "placeholder:text-text-muted",
          "transition-all duration-200",
          "focus:outline-none focus:border-neon-cyan focus:ring-2 focus:ring-neon-cyan/20",
          "disabled:cursor-not-allowed disabled:opacity-50",
          "hover:border-border-strong",
          className
        )}
        ref={ref}
        {...props}
      />
    )
  }
)
Input.displayName = "Input"

export { Input }
