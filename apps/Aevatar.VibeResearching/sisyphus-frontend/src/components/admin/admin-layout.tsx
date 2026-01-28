import React from "react"
import { Link, useNavigate } from "react-router-dom"
import { ArrowLeft } from "lucide-react"
import { AdminSidebar } from "./admin-sidebar"

// ============================================================
//  Admin Layout - Administration Panel Container
// ============================================================

interface AdminLayoutProps {
  children: React.ReactNode
  title: string
  subtitle?: string
  actions?: React.ReactNode
}

export const AdminLayout: React.FC<AdminLayoutProps> = ({
  children,
  title,
  subtitle,
  actions,
}) => {
  const navigate = useNavigate()

  return (
    <div className="min-h-screen bg-background">
      {/* Header */}
      <header className="h-16 border-b border-border-subtle bg-surface/80 backdrop-blur-xl sticky top-0 z-40">
        <div className="h-full flex items-center justify-between px-6">
          {/* Left - Back & Title */}
          <div className="flex items-center gap-4">
            <button
              onClick={() => navigate("/app")}
              className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-surface border border-border-subtle text-text-secondary hover:text-text-primary hover:border-border-default transition-all text-sm"
            >
              <ArrowLeft className="w-4 h-4" />
              <span>Back</span>
            </button>
            
            <div className="flex flex-col">
              <h1 className="text-lg font-display font-bold text-neon-gold tracking-wide">
                Administration
              </h1>
              <p className="text-xs text-text-muted">
                User and access management
              </p>
            </div>
          </div>

          {/* Right - Logo */}
          <Link to="/" className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-lg bg-neon-cyan flex items-center justify-center">
              <span className="text-sm font-display font-bold text-bg-base">S</span>
            </div>
            <span className="text-sm font-display font-semibold text-neon-cyan hidden sm:inline">
              SISYPHUS
            </span>
          </Link>
        </div>
      </header>

      {/* Main Content */}
      <div className="flex">
        {/* Sidebar */}
        <AdminSidebar />

        {/* Content Area */}
        <main className="flex-1 min-h-[calc(100vh-4rem)]">
          {/* Page Header */}
          <div className="border-b border-border-subtle bg-surface/50 px-6 py-4">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-xl font-semibold text-text-primary">{title}</h2>
                {subtitle && (
                  <p className="text-sm text-text-muted mt-0.5">{subtitle}</p>
                )}
              </div>
              {actions && (
                <div className="flex items-center gap-3">
                  {actions}
                </div>
              )}
            </div>
          </div>

          {/* Page Content */}
          <div className="p-6">
            {children}
          </div>
        </main>
      </div>
    </div>
  )
}

export default AdminLayout
