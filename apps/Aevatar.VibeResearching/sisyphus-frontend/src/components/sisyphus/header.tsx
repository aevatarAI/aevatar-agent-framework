import React from 'react';

const Header: React.FC = () => {
  return (
    <header className="h-16 flex items-center px-6 border-b border-border-subtle bg-surface/80 backdrop-blur-xl sticky top-0 z-50 neon-line-bottom">
      {/* Logo */}
      <div className="flex items-center gap-3">
        <div className="size-10 rounded-lg bg-neon-cyan flex items-center justify-center cyber-corners">
          <span className="text-bg-base text-lg font-bold font-display">S</span>
        </div>
        <div className="flex flex-col">
          <span className="text-lg font-display font-semibold text-neon-cyan text-glow-cyan tracking-wider text-balance">
            SISYPHUS
          </span>
          <span className="text-[10px] text-text-muted font-mono tracking-widest uppercase">
            Axiom Reasoning
          </span>
        </div>
      </div>
    </header>
  );
};

export default Header;
