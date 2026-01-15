import * as React from "react"
import { cn } from "@/lib/utils"

// ============================================================
//  Input Component - Cyberpunk Style
//  Based on ui-ux-pro-max Dark Mode + Neon accents
// ============================================================

const Input = React.forwardRef<HTMLInputElement, React.ComponentProps<"input">>(
  ({ className, type, ...props }, ref) => {
    return (
      <input
        type={type}
        className={cn(
          "flex h-10 w-full rounded-lg border border-[var(--border-default)] bg-quantum-surface px-3 py-2 text-sm text-[var(--text-primary)] font-body",
          "placeholder:text-[var(--text-muted)] placeholder:font-body",
          "transition-all duration-200",
          "focus:outline-none focus:border-quantum-cyan focus:ring-1 focus:ring-quantum-cyan/30 focus:shadow-glow-sm-cyan",
          "disabled:cursor-not-allowed disabled:opacity-50",
          "hover:border-quantum-cyan/50",
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
