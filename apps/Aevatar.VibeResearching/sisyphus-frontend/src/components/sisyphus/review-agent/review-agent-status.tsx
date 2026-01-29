import React from 'react';
import { cn } from '@/lib/utils';
import type { ReviewAgentStatusValue } from '@/types/review-agent';

interface ReviewAgentStatusProps {
  status: ReviewAgentStatusValue;
  currentIterationId?: string | null;
  lastCompletedAt?: string | null;
  nextScheduledAt?: string | null;
  errorMessage?: string | null;
}

const formatTimestamp = (iso: string | null | undefined): string => {
  if (!iso) return '-';
  try {
    const date = new Date(iso);
    return date.toLocaleString();
  } catch {
    return iso;
  }
};

const getStatusColor = (status: ReviewAgentStatusValue) => {
  switch (status) {
    case 'Idle':
      return 'text-neon-green';
    case 'WorkingReviewRound':
    case 'WorkingCleanupRound':
      return 'text-neon-cyan';
    case 'Error':
      return 'text-neon-red';
    default:
      return 'text-text-muted';
  }
};

const getStatusLabel = (status: ReviewAgentStatusValue) => {
  switch (status) {
    case 'Idle':
      return 'IDLE';
    case 'WorkingReviewRound':
      return 'REVIEWING';
    case 'WorkingCleanupRound':
      return 'CLEANUP';
    case 'Error':
      return 'ERROR';
    default:
      return status;
  }
};

const ReviewAgentStatus: React.FC<ReviewAgentStatusProps> = ({
  status,
  currentIterationId,
  lastCompletedAt,
  nextScheduledAt,
  errorMessage,
}) => {
  const statusColor = getStatusColor(status);
  const isWorking = status === 'WorkingReviewRound' || status === 'WorkingCleanupRound';
  const isError = status === 'Error';

  return (
    <div className="space-y-4">
      {/* Status Badge */}
      <div className="flex items-center gap-3">
        <div className={cn(
          "status-dot",
          isWorking ? "status-dot-active animate-pulse" :
          isError ? "bg-neon-red" : "status-dot-idle"
        )} />
        <span className={cn(
          "font-mono uppercase tracking-wider text-sm font-semibold",
          statusColor
        )}>
          {getStatusLabel(status)}
        </span>
        {typeof currentIterationId === 'string' && currentIterationId && (
          <span className="text-text-dimmed text-xs font-mono">
            [{currentIterationId.slice(0, 8)}...]
          </span>
        )}
      </div>

      {/* Error Message */}
      {isError && errorMessage && (
        <div className="p-3 rounded-lg bg-neon-red/10 border border-neon-red/30">
          <p className="text-neon-red text-sm font-mono">{errorMessage}</p>
        </div>
      )}

      {/* Timestamps */}
      <div className="grid grid-cols-2 gap-4">
        <div>
          <span className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">
            Last Completed
          </span>
          <span className="text-text-primary text-sm font-mono">
            {formatTimestamp(lastCompletedAt)}
          </span>
        </div>
        <div>
          <span className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">
            Next Scheduled
          </span>
          <span className="text-text-primary text-sm font-mono">
            {formatTimestamp(nextScheduledAt)}
          </span>
        </div>
      </div>
    </div>
  );
};

export default ReviewAgentStatus;
