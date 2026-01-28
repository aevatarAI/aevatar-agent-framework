import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

// ============================================================
//  Badge Component - Status and Role Badges
// ============================================================

const badgeVariants = cva(
  "inline-flex items-center gap-1.5 rounded px-2.5 py-1 text-xs font-semibold font-mono uppercase tracking-wide transition-colors",
  {
    variants: {
      variant: {
        default:
          "bg-border-subtle border border-border-default text-text-primary",
        cyan:
          "bg-neon-cyan/15 border border-neon-cyan/40 text-neon-cyan",
        gold:
          "bg-neon-gold/15 border border-neon-gold/40 text-neon-gold",
        purple:
          "bg-neon-purple/15 border border-neon-purple/40 text-neon-purple",
        green:
          "bg-neon-green/15 border border-neon-green/40 text-neon-green",
        red:
          "bg-neon-red/15 border border-neon-red/40 text-neon-red",
        orange:
          "bg-neon-orange/15 border border-neon-orange/40 text-neon-orange",
        // Status variants
        active:
          "bg-neon-green/15 border border-neon-green/40 text-neon-green",
        inactive:
          "bg-border-subtle border border-border-default text-text-muted",
        pending:
          "bg-neon-gold/15 border border-neon-gold/40 text-neon-gold",
        error:
          "bg-neon-red/15 border border-neon-red/40 text-neon-red",
        // Role variants
        admin:
          "bg-neon-gold/15 border border-neon-gold/40 text-neon-gold",
        member:
          "bg-neon-cyan/15 border border-neon-cyan/40 text-neon-cyan",
        viewer:
          "bg-neon-purple/15 border border-neon-purple/40 text-neon-purple",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

export interface BadgeProps
  extends React.HTMLAttributes<HTMLDivElement>,
    VariantProps<typeof badgeVariants> {}

const Badge = React.forwardRef<HTMLDivElement, BadgeProps>(
  ({ className, variant, ...props }, ref) => {
    return (
      <div
        ref={ref}
        className={cn(badgeVariants({ variant }), className)}
        {...props}
      />
    )
  }
)
Badge.displayName = "Badge"

// Status Dot Badge
export interface StatusDotProps {
  status: "active" | "inactive" | "pending" | "error" | "running"
  label?: string
  className?: string
}

const statusDotColors = {
  active: "bg-neon-green shadow-[0_0_8px_rgba(134,239,172,0.5)]",
  inactive: "bg-text-muted",
  pending: "bg-neon-gold shadow-[0_0_8px_rgba(252,211,77,0.5)]",
  error: "bg-neon-red shadow-[0_0_8px_rgba(252,165,165,0.5)]",
  running: "bg-neon-cyan shadow-[0_0_8px_rgba(125,211,252,0.5)] animate-pulse",
}

const StatusDot: React.FC<StatusDotProps> = ({ status, label, className }) => {
  return (
    <div className={cn("flex items-center gap-2", className)}>
      <span
        className={cn(
          "h-2.5 w-2.5 rounded-full",
          statusDotColors[status]
        )}
      />
      {label && (
        <span className="text-xs text-text-secondary capitalize">{label}</span>
      )}
    </div>
  )
}

export { Badge, badgeVariants, StatusDot }
