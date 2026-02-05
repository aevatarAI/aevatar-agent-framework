import * as React from "react"
import { Slot } from "@radix-ui/react-slot"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

// ============================================================
//  Button Component - Cyberpunk Style with Multiple Variants
// ============================================================

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-lg text-sm font-semibold transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-neon-cyan focus-visible:ring-offset-2 focus-visible:ring-offset-background disabled:pointer-events-none disabled:opacity-50",
  {
    variants: {
      variant: {
        default:
          "bg-neon-cyan text-bg-base hover:bg-neon-sky hover:shadow-glow-cyan",
        destructive:
          "bg-red-500 text-white hover:bg-red-600 hover:shadow-[0_0_12px_rgba(239,68,68,0.5)]",
        destructiveOutline:
          "bg-red-400/10 border border-red-400/30 text-red-400 hover:bg-red-400/20 hover:border-red-400/50",
        outline:
          "border border-neon-cyan bg-transparent text-neon-cyan hover:bg-neon-cyan/10 hover:shadow-glow-cyan",
        secondary:
          "bg-surface border border-border-default text-text-secondary hover:bg-surface-elevated hover:text-text-primary hover:border-border-strong",
        ghost:
          "text-text-secondary hover:bg-surface-elevated hover:text-text-primary",
        link:
          "text-neon-cyan underline-offset-4 hover:underline",
        gold:
          "bg-neon-gold text-bg-base hover:bg-amber-300 hover:shadow-glow-gold",
        purple:
          "bg-neon-purple text-bg-base hover:bg-violet-300 hover:shadow-glow-purple",
        success:
          "bg-neon-green text-bg-base hover:bg-green-300 hover:shadow-glow-green",
        successOutline:
          "bg-green-400/10 border border-green-400/30 text-green-400 hover:bg-green-400/20 hover:border-green-400/50",
      },
      size: {
        default: "h-10 px-4 py-2",
        sm: "h-9 rounded-md px-3 text-xs",
        lg: "h-11 rounded-lg px-8",
        icon: "h-10 w-10",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean
}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, ...props }, ref) => {
    const Comp = asChild ? Slot : "button"
    return (
      <Comp
        className={cn(buttonVariants({ variant, size, className }))}
        ref={ref}
        {...props}
      />
    )
  }
)
Button.displayName = "Button"

export { Button, buttonVariants }
