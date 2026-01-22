import { cva } from "class-variance-authority"

// ============================================================
//  Button Variants - Nebula Warm Theme
//  High contrast, warm accents for better readability
// ============================================================

export const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-lg text-sm font-semibold transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-neon-cyan focus-visible:ring-offset-2 focus-visible:ring-offset-background disabled:pointer-events-none disabled:opacity-50 cursor-pointer",
  {
    variants: {
      variant: {
        default:
          "bg-neon-cyan text-background hover:bg-neon-sky shadow-glow-cyan",
        destructive:
          "bg-neon-red text-white hover:bg-neon-red/90 shadow-glow-red",
        outline:
          "border border-neon-cyan bg-transparent text-neon-cyan hover:bg-neon-cyan/10",
        secondary:
          "bg-surface text-text-secondary hover:bg-surface-elevated border border-border hover:border-border-strong hover:text-text-primary",
        ghost:
          "text-text-secondary hover:text-text-primary hover:bg-surface-elevated",
        link: "text-neon-cyan underline-offset-4 hover:underline",
        warning:
          "bg-neon-gold text-background hover:bg-neon-amber shadow-glow-gold",
        success:
          "bg-neon-green text-background hover:bg-neon-green/90 shadow-glow-green",
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
