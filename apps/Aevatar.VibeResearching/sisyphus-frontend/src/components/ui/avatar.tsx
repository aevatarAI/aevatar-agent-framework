import * as React from "react"
import { cn } from "@/lib/utils"

// ============================================================
//  Avatar Component - With Initials Fallback & Dynamic Colors
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
  lg: "h-12 w-12 text-lg",
  xl: "h-16 w-16 text-xl",
}

// Color palette for avatar backgrounds (cyberpunk theme)
const avatarColors = [
  { bg: "bg-neon-cyan/20", border: "border-neon-cyan/30", text: "text-neon-cyan" },
  { bg: "bg-neon-purple/20", border: "border-neon-purple/30", text: "text-neon-purple" },
  { bg: "bg-neon-pink/20", border: "border-neon-pink/30", text: "text-neon-pink" },
  { bg: "bg-neon-gold/20", border: "border-neon-gold/30", text: "text-neon-gold" },
  { bg: "bg-neon-green/20", border: "border-neon-green/30", text: "text-neon-green" },
  { bg: "bg-neon-sky/20", border: "border-neon-sky/30", text: "text-neon-sky" },
  { bg: "bg-neon-orange/20", border: "border-neon-orange/30", text: "text-neon-orange" },
]

const getColorIndex = (name?: string): number => {
  if (!name) return 0
  // Simple hash function to get consistent color for same name
  let hash = 0
  for (let i = 0; i < name.length; i++) {
    hash = ((hash << 5) - hash) + name.charCodeAt(i)
    hash = hash & hash // Convert to 32bit integer
  }
  return Math.abs(hash) % avatarColors.length
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
    const colorScheme = avatarColors[getColorIndex(name)]

    return (
      <div
        ref={ref}
        className={cn(
          "relative flex shrink-0 overflow-hidden rounded-full",
          showFallback ? [colorScheme.bg, colorScheme.border] : "bg-bg-elevated border-border-subtle",
          "border",
          sizeClasses[size],
          className
        )}
      >
        {showFallback ? (
          <div className={cn("flex h-full w-full items-center justify-center", colorScheme.bg)}>
            <span className={cn("font-display font-semibold", colorScheme.text)}>
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
