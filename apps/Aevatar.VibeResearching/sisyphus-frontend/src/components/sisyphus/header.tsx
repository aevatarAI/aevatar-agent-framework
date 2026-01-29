import React, { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { Shield, User, Users, Key, LogOut, Settings, ChevronDown } from 'lucide-react';
import { cn } from '@/lib/utils';
import { getReviewAgentStatus } from '@/lib/axiom-client';
import { ReviewAgentDashboard } from './review-agent';
import { Avatar } from '@/components/ui/avatar';
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuLabel } from '@/components/ui/dropdown-menu';
import { useAuthStore, useIsAdmin } from '@/store/auth-store';

// Lightweight status polling for header indicator
function useReviewAgentStatusIndicator() {
  const [isWorking, setIsWorking] = useState(false);

  useEffect(() => {
    const checkStatus = async () => {
      try {
        const data = await getReviewAgentStatus();
        const status = data.status ?? 'Idle';
        // Non-idle statuses: WorkingReviewRound, WorkingCleanupRound, Error
        setIsWorking(status !== 'Idle');
      } catch {
        // Silently ignore errors
      }
    };

    // Check immediately
    checkStatus();

    // Poll every 5 seconds
    const interval = setInterval(checkStatus, 5000);
    return () => clearInterval(interval);
  }, []);

  return isWorking;
}

const Header: React.FC = () => {
  const navigate = useNavigate();
  const [isReviewAgentOpen, setIsReviewAgentOpen] = useState(false);
  const isReviewAgentWorking = useReviewAgentStatusIndicator();
  
  // Auth state
  const { user, isAuthenticated, logout } = useAuthStore();
  const isAdmin = useIsAdmin();
  
  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  return (
    <>
      <header className="h-16 flex items-center justify-between px-6 border-b border-border-subtle bg-surface/80 backdrop-blur-xl sticky top-0 z-50 neon-line-bottom">
        {/* Logo - Click to go home */}
        <button
          onClick={() => navigate('/')}
          className="flex items-center gap-3 hover:opacity-80 transition-opacity cursor-pointer"
          aria-label="Go to home page"
        >
          <div className="size-10 rounded-lg bg-neon-cyan flex items-center justify-center cyber-corners">
            <span className="text-bg-base text-lg font-bold font-display">S</span>
          </div>
          <div className="flex flex-col text-left">
            <span className="text-lg font-display font-semibold text-neon-cyan text-glow-cyan tracking-wider text-balance">
              SISYPHUS
            </span>
            <span className="text-[10px] text-text-muted font-mono tracking-widest uppercase">
              Axiom Reasoning
            </span>
          </div>
        </button>

        {/* Right side - Review Agent button + User Menu */}
        <div className="flex items-center gap-3">
          {/* Review Agent Button */}
          <button
            onClick={() => setIsReviewAgentOpen(true)}
            className={cn(
              "flex items-center gap-2 px-3 py-2 rounded-lg",
              "border transition-all duration-200",
              isReviewAgentWorking
                ? "bg-neon-orange/10 border-neon-orange/50 text-neon-orange shadow-[0_0_12px_rgba(249,115,22,0.3)]"
                : "bg-bg-elevated hover:bg-bg-elevated/80 border-border-subtle text-text-muted hover:text-neon-cyan"
            )}
            style={isReviewAgentWorking ? { animation: 'pulse 3s cubic-bezier(0.4, 0, 0.6, 1) infinite' } : undefined}
            aria-label="Open Review Agent Dashboard"
          >
            <Shield className="w-4 h-4" />
            <span className="text-xs font-mono uppercase tracking-wider">
              Review Agent
            </span>
            {isReviewAgentWorking && (
              <span className="relative flex h-2 w-2">
                <span
                  className="absolute inline-flex h-full w-full rounded-full bg-neon-orange opacity-75"
                  style={{ animation: 'ping 2.5s cubic-bezier(0, 0, 0.2, 1) infinite' }}
                ></span>
                <span className="relative inline-flex rounded-full h-2 w-2 bg-neon-orange"></span>
              </span>
            )}
          </button>

          {/* User Menu */}
          {isAuthenticated && user ? (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <button className="flex items-center gap-2 px-2 py-1.5 rounded-lg hover:bg-surface-elevated transition-colors">
                  <Avatar
                    name={user.name || user.userName}
                    src={user.avatarUrl}
                    size="sm"
                  />
                  <div className="hidden sm:flex flex-col items-start">
                    <span className="text-sm font-medium text-text-primary">
                      {user.name || user.userName}{user.surname ? ` ${user.surname}` : ''}
                    </span>
                    <span className="text-[10px] text-text-muted">
                      {isAdmin ? 'Administrator' : 'Member'}
                    </span>
                  </div>
                  <ChevronDown className="w-4 h-4 text-text-muted" />
                </button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-56">
                {/* Admin Section */}
                {isAdmin && (
                  <>
                    <DropdownMenuLabel className="text-neon-gold">
                      Administration
                    </DropdownMenuLabel>
                    <DropdownMenuItem
                      onClick={() => navigate('/admin/users')}
                      icon={<Users className="w-4 h-4" />}
                    >
                      Users
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      onClick={() => navigate('/admin/roles')}
                      icon={<Shield className="w-4 h-4" />}
                    >
                      Roles
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      onClick={() => navigate('/admin/permissions')}
                      icon={<Key className="w-4 h-4" />}
                    >
                      Permissions
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                  </>
                )}

                {/* Account Section */}
                <DropdownMenuItem
                  onClick={() => navigate('/account/profile')}
                  icon={<User className="w-4 h-4" />}
                >
                  My Account
                </DropdownMenuItem>
                <DropdownMenuItem
                  onClick={() => navigate('/account/password')}
                  icon={<Settings className="w-4 h-4" />}
                >
                  Settings
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  onClick={handleLogout}
                  destructive
                  icon={<LogOut className="w-4 h-4" />}
                >
                  Sign Out
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          ) : (
            <Link
              to="/login"
              className="flex items-center gap-2 px-3 py-2 rounded-lg bg-neon-cyan text-bg-base text-sm font-semibold hover:bg-neon-sky transition-colors"
            >
              Sign In
            </Link>
          )}
        </div>
      </header>

      {/* Review Agent Dashboard Panel */}
      <ReviewAgentDashboard
        isOpen={isReviewAgentOpen}
        onClose={() => setIsReviewAgentOpen(false)}
      />
    </>
  );
};

export default Header;
