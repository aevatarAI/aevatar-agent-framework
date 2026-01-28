import * as React from "react"
import { cn } from "@/lib/utils"

// ============================================================
//  Avatar Component - With Initials Fallback
// ============================================================

export interface AvatarProps {
  src?: string | null
  alt?: string
  name?: string
  size?: "sm" | "md" | "lg" | "xl"
  className?: string
}

const sizeClasses = {
  sm: "h-8 w-8 text-xs",
  md: "h-10 w-10 text-sm",
  lg: "h-12 w-12 text-base",
  xl: "h-16 w-16 text-lg",
}

const getInitials = (name?: string): string => {
  if (!name) return "?"
  const parts = name.trim().split(/\s+/)
  if (parts.length === 1) {
    return parts[0].charAt(0).toUpperCase()
  }
  return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase()
}

const Avatar = React.forwardRef<HTMLDivElement, AvatarProps>(
  ({ src, alt, name, size = "md", className }, ref) => {
    const [imageError, setImageError] = React.useState(false)

    const showFallback = !src || imageError

    return (
      <div
        ref={ref}
        className={cn(
          "relative flex shrink-0 overflow-hidden rounded-full",
          "bg-neon-cyan/20 border border-neon-cyan/30",
          sizeClasses[size],
          className
        )}
      >
        {showFallback ? (
          <div className="flex h-full w-full items-center justify-center bg-neon-cyan/20">
            <span className="font-display font-semibold text-neon-cyan">
              {getInitials(name)}
            </span>
          </div>
        ) : (
          <img
            src={src}
            alt={alt || name || "Avatar"}
            onError={() => setImageError(true)}
            className="aspect-square h-full w-full object-cover"
          />
        )}
      </div>
    )
  }
)
Avatar.displayName = "Avatar"

// Avatar with Status Indicator
export interface AvatarWithStatusProps extends AvatarProps {
  status?: "online" | "offline" | "away" | "busy"
}

const statusColors = {
  online: "bg-neon-green",
  offline: "bg-text-muted",
  away: "bg-neon-gold",
  busy: "bg-neon-red",
}

const AvatarWithStatus = React.forwardRef<HTMLDivElement, AvatarWithStatusProps>(
  ({ status, className, ...props }, ref) => {
    return (
      <div ref={ref} className={cn("relative inline-block", className)}>
        <Avatar {...props} />
        {status && (
          <span
            className={cn(
              "absolute bottom-0 right-0 block h-2.5 w-2.5 rounded-full ring-2 ring-bg-base",
              statusColors[status]
            )}
          />
        )}
      </div>
    )
  }
)
AvatarWithStatus.displayName = "AvatarWithStatus"

export { Avatar, AvatarWithStatus }
