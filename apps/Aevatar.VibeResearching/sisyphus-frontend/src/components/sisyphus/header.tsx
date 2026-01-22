import React from 'react';
import { useNavigate } from 'react-router-dom';

const Header: React.FC = () => {
  const navigate = useNavigate();

  return (
    <header className="h-16 flex items-center px-6 border-b border-border-subtle bg-surface/80 backdrop-blur-xl sticky top-0 z-50 neon-line-bottom">
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
    </header>
  );
};

export default Header;
