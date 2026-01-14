import { cva } from "class-variance-authority"

// ============================================================
//  Button Variants - Cyberpunk + Quantum Style
//  Based on ui-ux-pro-max Cyberpunk UI guidelines
// ============================================================

export const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-lg text-sm font-semibold transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-quantum-cyan focus-visible:ring-offset-2 focus-visible:ring-offset-quantum-dark disabled:pointer-events-none disabled:opacity-50 cursor-pointer font-hud uppercase tracking-wider",
  {
    variants: {
      variant: {
        default:
          "bg-quantum-cyan text-quantum-dark hover:bg-quantum-cyan/90 shadow-glow-sm-cyan",
        destructive:
          "bg-ai-error text-white hover:bg-ai-error/90 shadow-[0_0_10px_rgba(239,68,68,0.3)]",
        outline:
          "border border-quantum-cyan/50 bg-transparent text-quantum-cyan hover:bg-quantum-cyan/10 hover:border-quantum-cyan",
        secondary:
          "bg-quantum-panel text-[var(--text-secondary)] hover:bg-quantum-elevated border border-[var(--border-default)] hover:border-quantum-cyan/30",
        ghost:
          "text-[var(--text-muted)] hover:text-quantum-cyan hover:bg-quantum-cyan/10",
        link: "text-quantum-cyan underline-offset-4 hover:underline",
        neon:
          "bg-transparent border border-quantum-magenta text-quantum-magenta hover:bg-quantum-magenta/10 shadow-glow-magenta",
      },
      size: {
        default: "h-10 px-4 py-2",
        sm: "h-8 px-3 text-xs",
        lg: "h-12 px-6 text-base",
        icon: "h-10 w-10",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)
