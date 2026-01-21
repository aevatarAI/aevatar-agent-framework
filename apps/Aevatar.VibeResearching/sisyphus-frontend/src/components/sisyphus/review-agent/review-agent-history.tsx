import React, { useEffect, useState } from 'react';
import { Clock, CheckCircle, XCircle, ChevronRight, Loader2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { IterationListResponse, ReviewIterationSummary } from '@/types/review-agent';

interface ReviewAgentHistoryProps {
  onSelectIteration?: (iterationId: string) => void;
}

const ReviewAgentHistory: React.FC<ReviewAgentHistoryProps> = ({ onSelectIteration }) => {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<IterationListResponse | null>(null);
  const [page, setPage] = useState(0);
  const limit = 10;

  useEffect(() => {
    fetchIterations();
  }, [page]);

  const fetchIterations = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`/api/review-agent/iterations?limit=${limit}&offset=${page * limit}`);
      if (!response.ok) {
        throw new Error(`Failed to fetch iterations: ${response.status}`);
      }
      const result: IterationListResponse = await response.json();
      setData(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  if (loading && !data) {
    return (
      <div className="flex items-center justify-center py-8">
        <Loader2 className="w-6 h-6 text-neon-cyan animate-spin" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4 rounded-lg bg-neon-red/10 border border-neon-red/30">
        <p className="text-neon-red text-sm">{error}</p>
      </div>
    );
  }

  if (!data || data.iterations.length === 0) {
    return (
      <div className="text-center py-8">
        <Clock className="w-12 h-12 mx-auto text-text-muted mb-3 opacity-50" />
        <p className="text-text-muted">No review iterations yet</p>
        <p className="text-text-dimmed text-sm mt-1">
          Iterations will appear here after the first review round completes.
        </p>
      </div>
    );
  }

  const totalPages = Math.ceil(data.total / limit);

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        {data.iterations.map((iteration) => (
          <IterationCard
            key={iteration.iterationId}
            iteration={iteration}
            onClick={() => onSelectIteration?.(iteration.iterationId)}
          />
        ))}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between pt-2 border-t border-border-subtle">
          <button
            onClick={() => setPage((p) => Math.max(0, p - 1))}
            disabled={page === 0}
            className="px-3 py-1 text-sm rounded bg-surface-elevated hover:opacity-80 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Previous
          </button>
          <span className="text-text-muted text-sm">
            Page {page + 1} of {totalPages}
          </span>
          <button
            onClick={() => setPage((p) => Math.min(totalPages - 1, p + 1))}
            disabled={page >= totalPages - 1}
            className="px-3 py-1 text-sm rounded bg-surface-elevated hover:opacity-80 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Next
          </button>
        </div>
      )}
    </div>
  );
};

interface IterationCardProps {
  iteration: ReviewIterationSummary;
  onClick?: () => void;
}

const IterationCard: React.FC<IterationCardProps> = ({ iteration, onClick }) => {
  const hasDeactivated = iteration.nodesDeactivated > 0;

  return (
    <button
      onClick={onClick}
      className={cn(
        "w-full text-left p-3 rounded-lg border transition-all",
        "bg-surface-elevated hover:opacity-80",
        hasDeactivated
          ? "border-neon-orange/30 hover:border-neon-orange/50"
          : "border-border-subtle hover:border-neon-cyan/30"
      )}
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          {hasDeactivated ? (
            <XCircle className="w-4 h-4 text-neon-orange" />
          ) : (
            <CheckCircle className="w-4 h-4 text-neon-green" />
          )}
          <span className="text-text-primary font-mono text-sm">
            {iteration.iterationId}
          </span>
        </div>
        <ChevronRight className="w-4 h-4 text-text-muted" />
      </div>

      <div className="mt-2 flex items-center gap-4 text-xs text-text-muted">
        <span>{new Date(iteration.startedAt).toLocaleDateString()}</span>
        <span>
          {iteration.completedAt
            ? `Duration: ${formatDuration(iteration.startedAt, iteration.completedAt)}`
            : 'In progress'}
        </span>
      </div>

      <div className="mt-2 flex items-center gap-3">
        <div className="flex items-center gap-1">
          <span className="text-text-dimmed text-xs">Reviewed:</span>
          <span className="text-neon-cyan text-xs font-semibold">{iteration.nodesReviewed}</span>
        </div>
        <div className="flex items-center gap-1">
          <span className="text-text-dimmed text-xs">Valid:</span>
          <span className="text-neon-green text-xs font-semibold">{iteration.nodesValid}</span>
        </div>
        {hasDeactivated && (
          <div className="flex items-center gap-1">
            <span className="text-text-dimmed text-xs">Deactivated:</span>
            <span className="text-neon-orange text-xs font-semibold">{iteration.nodesDeactivated}</span>
          </div>
        )}
      </div>
    </button>
  );
};

const formatDuration = (startedAt: string, completedAt: string | null) => {
  if (!completedAt) return 'In progress';
  const start = new Date(startedAt).getTime();
  const end = new Date(completedAt).getTime();
  const durationMs = end - start;
  const seconds = Math.floor(durationMs / 1000);
  const minutes = Math.floor(seconds / 60);
  if (minutes > 0) {
    return `${minutes}m ${seconds % 60}s`;
  }
  return `${seconds}s`;
};

export default ReviewAgentHistory;
