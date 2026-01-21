import React, { useState, useEffect } from 'react';
import { cn } from '@/lib/utils';
import ReviewProgressPopup from './review-progress-popup';

interface ReviewAgentProgressProps {
  nodesReviewed: number;
  nodesPending: number;
  nodesDeactivated: number;
  nodesRemoved?: number;
  estimatedCompletionTime?: Date | null;
  iterationStartTime?: number | null;
  currentNodeId?: string | null;
}


interface CounterCardProps {
  label: string;
  value: number;
  color: 'cyan' | 'gold' | 'red' | 'purple';
  highlight?: boolean;
  onClick?: () => void;
}

const CounterCard: React.FC<CounterCardProps> = ({ label, value, color, highlight, onClick }) => {
  const colorClasses = {
    cyan: 'text-neon-cyan border-neon-cyan/30 bg-neon-cyan/5 hover:bg-neon-cyan/10 hover:border-neon-cyan/50',
    gold: 'text-neon-gold border-neon-gold/30 bg-neon-gold/5 hover:bg-neon-gold/10 hover:border-neon-gold/50',
    red: 'text-neon-red border-neon-red/30 bg-neon-red/5 hover:bg-neon-red/10 hover:border-neon-red/50',
    purple: 'text-neon-purple border-neon-purple/30 bg-neon-purple/5 hover:bg-neon-purple/10 hover:border-neon-purple/50',
  };

  return (
    <button
      onClick={onClick}
      className={cn(
        "p-3 rounded-lg border transition-all duration-300 text-left w-full cursor-pointer",
        "active:scale-[0.98]",
        colorClasses[color],
        highlight && "ring-1 ring-offset-2 ring-offset-background"
      )}
      title={`Click to view ${label.toLowerCase()} nodes in graph`}
    >
      <span className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">
        {label}
      </span>
      <span className={cn(
        "text-2xl font-mono font-bold",
        highlight && "animate-pulse"
      )}>
        {(value ?? 0).toLocaleString()}
      </span>
    </button>
  );
};

const ReviewAgentProgress: React.FC<ReviewAgentProgressProps> = ({
  nodesReviewed,
  nodesPending,
  nodesDeactivated,
  nodesRemoved = 0,
  iterationStartTime,
  currentNodeId,
}) => {
  const isWorking = nodesPending > 0;
  const percentage = nodesReviewed + nodesPending > 0
    ? Math.round((nodesReviewed / (nodesReviewed + nodesPending)) * 100)
    : 0;

  // Popup state
  const [popupOpen, setPopupOpen] = useState(false);

  // Real-time ETA calculation with periodic updates
  const [etaDisplay, setEtaDisplay] = useState<string | null>(null);
  const [localStartTime, setLocalStartTime] = useState<number | null>(null);

  // Track start time locally if not provided (for when panel opens mid-review)
  useEffect(() => {
    if (isWorking && nodesReviewed > 0 && !iterationStartTime && !localStartTime) {
      // Estimate start time based on average review time of 30 seconds per node
      const estimatedElapsed = nodesReviewed * 30 * 1000;
      setLocalStartTime(Date.now() - estimatedElapsed);
    }
    if (!isWorking) {
      setLocalStartTime(null);
    }
  }, [isWorking, nodesReviewed, iterationStartTime, localStartTime]);

  const effectiveStartTime = iterationStartTime || localStartTime;

  useEffect(() => {
    const calculateEta = () => {
      if (!effectiveStartTime || nodesReviewed === 0 || nodesPending === 0) {
        setEtaDisplay(null);
        return;
      }

      const elapsedMs = Date.now() - effectiveStartTime;
      const reviewSpeedMsPerNode = elapsedMs / nodesReviewed;
      const remainingMs = nodesPending * reviewSpeedMsPerNode;
      const etaTimestamp = Date.now() + remainingMs;
      const etaDate = new Date(etaTimestamp);

      // Format as HH:MM
      setEtaDisplay(etaDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }));
    };

    // Calculate immediately
    calculateEta();

    // Update every 5 seconds while working
    if (isWorking && effectiveStartTime && nodesReviewed > 0) {
      const interval = setInterval(calculateEta, 5000);
      return () => clearInterval(interval);
    }
  }, [effectiveStartTime, nodesReviewed, nodesPending, isWorking]);

  // Open graph popup
  const handleCounterClick = () => {
    setPopupOpen(true);
  };

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h3 className="text-text-dimmed text-xs uppercase tracking-wider font-semibold">
          Progress Counters
        </h3>
        <span className="text-text-dimmed text-[10px] font-mono">
          Click to view graph
        </span>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <CounterCard
          label="Reviewed"
          value={nodesReviewed}
          color="cyan"
          highlight={isWorking}
          onClick={handleCounterClick}
        />
        <CounterCard
          label="Pending"
          value={nodesPending}
          color="gold"
          highlight={isWorking && nodesPending > 0}
          onClick={handleCounterClick}
        />
        <CounterCard
          label="Deactivated"
          value={nodesDeactivated}
          color="red"
          onClick={handleCounterClick}
        />
        <CounterCard
          label="Removed"
          value={nodesRemoved}
          color="purple"
          onClick={handleCounterClick}
        />
      </div>

      {/* Progress Bar (when working) */}
      {isWorking && nodesReviewed + nodesPending > 0 && (
        <div className="mt-4">
          <div className="flex justify-between items-center text-xs text-text-dimmed mb-1">
            <span>Progress</span>
            <div className="flex items-center gap-3">
              <span className="font-mono">{percentage}%</span>
              {etaDisplay && (
                <span className="text-neon-cyan font-mono">
                  ETA {etaDisplay}
                </span>
              )}
            </div>
          </div>
          <div className="h-2 bg-surface-elevated rounded-full overflow-hidden">
            <div
              className="h-full bg-gradient-to-r from-neon-cyan to-neon-green transition-all duration-300"
              style={{ width: `${percentage}%` }}
            />
          </div>
        </div>
      )}

      {/* Review Progress Popup */}
      <ReviewProgressPopup
        open={popupOpen}
        onOpenChange={setPopupOpen}
        currentNodeId={currentNodeId}
      />
    </div>
  );
};

export default ReviewAgentProgress;
