import React, { useEffect, useState } from 'react';
import { ArrowLeft, CheckCircle, XCircle, AlertCircle, Loader2, FileText } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { ReviewIteration, ReviewLogEntry, ReviewResult } from '@/types/review-agent';

interface ReviewAgentIterationDetailProps {
  iterationId: string;
  onBack?: () => void;
}

const ReviewAgentIterationDetail: React.FC<ReviewAgentIterationDetailProps> = ({
  iterationId,
  onBack,
}) => {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [iteration, setIteration] = useState<ReviewIteration | null>(null);
  const [selectedEntry, setSelectedEntry] = useState<ReviewLogEntry | null>(null);

  useEffect(() => {
    fetchIteration();
  }, [iterationId]);

  const fetchIteration = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`/api/review-agent/iterations/${iterationId}`);
      if (!response.ok) {
        if (response.status === 404) {
          throw new Error('Iteration not found');
        }
        throw new Error(`Failed to fetch iteration: ${response.status}`);
      }
      const result: ReviewIteration = await response.json();
      setIteration(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
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

  if (loading) {
    return (
      <div className="flex items-center justify-center py-8">
        <Loader2 className="w-6 h-6 text-neon-cyan animate-spin" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-4">
        {onBack && (
          <button
            onClick={onBack}
            className="flex items-center gap-2 text-text-muted hover:text-text-primary transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            <span>Back to history</span>
          </button>
        )}
        <div className="p-4 rounded-lg bg-neon-red/10 border border-neon-red/30">
          <p className="text-neon-red text-sm">{error}</p>
        </div>
      </div>
    );
  }

  if (!iteration) return null;

  return (
    <div className="space-y-4">
      {/* Header */}
      {onBack && (
        <button
          onClick={onBack}
          className="flex items-center gap-2 text-text-muted hover:text-text-primary transition-colors"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>Back to history</span>
        </button>
      )}

      {/* Summary Card */}
      <div className="p-4 rounded-lg bg-surface-elevated border border-border-subtle">
        <h3 className="text-text-primary font-mono font-semibold mb-3">
          Iteration: {iteration.iterationId}
        </h3>

        <div className="grid grid-cols-2 gap-3 text-sm">
          <div>
            <span className="text-text-dimmed">Started:</span>
            <span className="text-text-primary ml-2">{formatDate(iteration.startedAt)}</span>
          </div>
          <div>
            <span className="text-text-dimmed">Completed:</span>
            <span className="text-text-primary ml-2">
              {iteration.completedAt ? formatDate(iteration.completedAt) : 'In progress'}
            </span>
          </div>
          <div>
            <span className="text-text-dimmed">Duration:</span>
            <span className="text-text-primary ml-2">
              {formatDuration(iteration.startedAt, iteration.completedAt)}
            </span>
          </div>
        </div>

        <div className="flex items-center gap-4 mt-4 pt-3 border-t border-border-subtle">
          <StatBadge label="Reviewed" value={iteration.nodesReviewed} color="cyan" />
          <StatBadge label="Valid" value={iteration.nodesValid} color="green" />
          <StatBadge label="Deactivated" value={iteration.nodesDeactivated} color="orange" />
          {iteration.nodesRemoved > 0 && (
            <StatBadge label="Removed" value={iteration.nodesRemoved} color="red" />
          )}
        </div>
      </div>

      {/* Entries List */}
      <div>
        <h4 className="text-text-dimmed text-xs uppercase tracking-wider font-semibold mb-3">
          Review Log ({iteration.entries.length} entries)
        </h4>

        {iteration.entries.length === 0 ? (
          <p className="text-text-muted text-sm">No entries in this iteration.</p>
        ) : (
          <div className="space-y-2 max-h-64 overflow-y-auto">
            {iteration.entries.map((entry) => (
              <EntryCard
                key={entry.entryId}
                entry={entry}
                isSelected={selectedEntry?.entryId === entry.entryId}
                onClick={() => setSelectedEntry(selectedEntry?.entryId === entry.entryId ? null : entry)}
              />
            ))}
          </div>
        )}
      </div>

      {/* Selected Entry Detail */}
      {selectedEntry && (
        <div className="p-4 rounded-lg bg-surface border border-neon-cyan/30">
          <div className="flex items-center justify-between mb-3">
            <h4 className="text-neon-cyan font-semibold">Entry Details</h4>
            <button
              onClick={() => setSelectedEntry(null)}
              className="text-text-muted hover:text-text-primary text-sm"
            >
              Close
            </button>
          </div>

          <div className="space-y-3 text-sm">
            <div>
              <span className="text-text-dimmed">Node ID:</span>
              <code className="text-text-primary ml-2 font-mono text-xs bg-background px-2 py-0.5 rounded">
                {selectedEntry.nodeId}
              </code>
            </div>
            <div>
              <span className="text-text-dimmed">Label:</span>
              <span className="text-text-primary ml-2">{selectedEntry.nodeLabel}</span>
            </div>
            <div>
              <span className="text-text-dimmed">Result:</span>
              <ResultBadge result={selectedEntry.reviewResult} className="ml-2" />
            </div>

            {selectedEntry.deactivatedReason && (
              <div>
                <span className="text-text-dimmed">Deactivation Reason:</span>
                <p className="text-neon-orange mt-1 text-sm">{selectedEntry.deactivatedReason}</p>
              </div>
            )}

            {selectedEntry.dependencies.length > 0 && (
              <div>
                <span className="text-text-dimmed">Dependencies:</span>
                <div className="mt-1 flex flex-wrap gap-1">
                  {selectedEntry.dependencies.map((dep) => (
                    <code
                      key={dep}
                      className="text-xs font-mono bg-background px-1.5 py-0.5 rounded text-text-muted"
                    >
                      {dep}
                    </code>
                  ))}
                </div>
              </div>
            )}

            {selectedEntry.verificationContent && (
              <div>
                <span className="text-text-dimmed flex items-center gap-1">
                  <FileText className="w-3 h-3" />
                  Verification Content:
                </span>
                <pre className="mt-1 p-2 rounded bg-background text-text-muted text-xs font-mono overflow-x-auto max-h-32 whitespace-pre-wrap">
                  {selectedEntry.verificationContent}
                </pre>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
};

interface StatBadgeProps {
  label: string;
  value: number;
  color: 'cyan' | 'green' | 'orange' | 'red';
}

const StatBadge: React.FC<StatBadgeProps> = ({ label, value, color }) => {
  const colorClasses = {
    cyan: 'text-neon-cyan',
    green: 'text-neon-green',
    orange: 'text-neon-orange',
    red: 'text-neon-red',
  };

  return (
    <div className="flex items-center gap-1.5">
      <span className="text-text-dimmed text-xs">{label}:</span>
      <span className={cn('font-semibold', colorClasses[color])}>{value}</span>
    </div>
  );
};

interface EntryCardProps {
  entry: ReviewLogEntry;
  isSelected: boolean;
  onClick: () => void;
}

const EntryCard: React.FC<EntryCardProps> = ({ entry, isSelected, onClick }) => {
  return (
    <button
      onClick={onClick}
      className={cn(
        "w-full text-left p-2 rounded border transition-all",
        isSelected
          ? "bg-neon-cyan/10 border-neon-cyan/50"
          : "bg-surface-elevated border-border-subtle hover:border-neon-cyan/30"
      )}
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2 min-w-0">
          <ResultIcon result={entry.reviewResult} />
          <span className="text-text-primary text-sm truncate">{entry.nodeLabel}</span>
        </div>
        <span className="text-text-dimmed text-xs whitespace-nowrap ml-2">
          {new Date(entry.timestamp).toLocaleTimeString()}
        </span>
      </div>
    </button>
  );
};

interface ResultIconProps {
  result: ReviewResult;
}

const ResultIcon: React.FC<ResultIconProps> = ({ result }) => {
  switch (result) {
    case 'Passed':
      return <CheckCircle className="w-4 h-4 text-neon-green flex-shrink-0" />;
    case 'Failed':
      return <XCircle className="w-4 h-4 text-neon-orange flex-shrink-0" />;
    case 'Skipped':
      return <AlertCircle className="w-4 h-4 text-text-muted flex-shrink-0" />;
  }
};

interface ResultBadgeProps {
  result: ReviewResult;
  className?: string;
}

const ResultBadge: React.FC<ResultBadgeProps> = ({ result, className }) => {
  const config = {
    Passed: { color: 'text-neon-green bg-neon-green/10 border-neon-green/30', label: 'Passed' },
    Failed: { color: 'text-neon-orange bg-neon-orange/10 border-neon-orange/30', label: 'Failed' },
    Skipped: { color: 'text-text-muted bg-surface-elevated border-border-subtle', label: 'Skipped' },
  };

  return (
    <span
      className={cn(
        'inline-flex items-center px-2 py-0.5 rounded text-xs font-semibold border',
        config[result].color,
        className
      )}
    >
      {config[result].label}
    </span>
  );
};

export default ReviewAgentIterationDetail;
