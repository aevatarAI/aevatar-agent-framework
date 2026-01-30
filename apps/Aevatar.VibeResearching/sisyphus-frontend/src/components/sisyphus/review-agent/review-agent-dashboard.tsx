import React, { useState, useEffect, useRef, Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import { X, RefreshCw, Settings, Wifi, WifiOff, History, Activity, AlertTriangle, ChevronLeft, ChevronRight, Bot, Table2, Download, Play, Loader2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useReviewAgent } from '@/hooks/use-review-agent';
import ReviewAgentStatus from './review-agent-status';
import ReviewAgentProgress from './review-agent-progress';
import ReviewAgentHistory from './review-agent-history';
import ReviewAgentIterationDetail from './review-agent-iteration-detail';
import ReviewAgentSettingsPanel from './review-agent-settings';

// Error Boundary to catch runtime errors
interface ErrorBoundaryProps {
  children: ReactNode;
  onClose: () => void;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

class ReviewAgentErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('[ReviewAgentDashboard] Error caught by boundary:', error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="fixed inset-0 z-50 flex items-start justify-end pt-20 pr-4">
          <div
            className="absolute inset-0 backdrop-blur-sm"
            style={{ backgroundColor: 'rgba(12, 15, 20, 0.5)' }}
            onClick={this.props.onClose}
          />
          <div
            className="relative w-[420px] p-6 rounded-lg border border-neon-red/50"
            style={{ backgroundColor: 'rgba(20, 24, 32, 0.95)' }}
          >
            <div className="flex items-center gap-3 mb-4">
              <AlertTriangle className="w-6 h-6 text-neon-red" />
              <h2 className="font-display text-lg font-semibold text-neon-red">
                Review Agent Error
              </h2>
            </div>
            <p className="text-text-primary text-sm mb-2">
              An error occurred while rendering the Review Agent panel:
            </p>
            <pre className="p-3 rounded bg-background text-neon-red text-xs font-mono overflow-auto max-h-40 mb-4">
              {this.state.error?.message || 'Unknown error'}
            </pre>
            <button
              onClick={this.props.onClose}
              className="w-full px-4 py-2 rounded-lg bg-surface-elevated hover:opacity-80 border border-border-subtle transition-colors text-sm"
            >
              Close
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}

interface ReviewAgentDashboardProps {
  isOpen: boolean;
  onClose: () => void;
}

// Auto-scrolling token card component - fixed size
interface TokenCardProps {
  agentId: string;
  agentRole: 'coordinator' | 'worker';
  tokens: string;
  isComplete: boolean;
  index: number;
}

const TokenCard: React.FC<TokenCardProps> = ({ agentId, agentRole, tokens, isComplete, index }) => {
  const scrollRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to bottom when tokens change
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [tokens]);

  return (
    <div
      className={cn(
        "w-[320px] h-[340px] p-3 rounded-lg border flex flex-col",
        agentRole === 'coordinator'
          ? "bg-neon-purple/10 border-neon-purple/30"
          : "bg-neon-cyan/10 border-neon-cyan/30"
      )}
    >
      <div className="flex-shrink-0 flex items-center justify-between mb-2">
        <span className={cn(
          "text-xs font-semibold uppercase truncate",
          agentRole === 'coordinator' ? "text-neon-purple" : "text-neon-cyan"
        )}>
          {agentRole === 'coordinator' ? 'COORDINATOR' : (
            agentId?.match(/worker-(\d+)/)?.[0] || `WORKER-${index}`
          )}
        </span>
        {!isComplete && (
          <div className="w-2 h-2 rounded-full bg-neon-green animate-pulse flex-shrink-0" />
        )}
      </div>
      <div
        ref={scrollRef}
        className="flex-1 min-h-0 text-text-primary text-xs font-mono whitespace-pre-wrap overflow-y-auto"
      >
        {tokens || ''}
        {!isComplete && <span className="animate-pulse text-neon-green">|</span>}
      </div>
    </div>
  );
};

// Review Log Entry Detail type (extended from SSE data)
interface ReviewLogEntryDetail {
  nodeId: string;
  sessionId?: string | null;
  nodeLabel: string;
  coreDescription?: string | null;
  explainContent?: string | null;
  result: 'Passed' | 'Failed' | 'Skipped' | null;
  timestamp: number;
  deactivatedReason?: string | null;
  verificationContent?: string | null;
}

// Review Log Detail Popup Component
interface ReviewLogPopupProps {
  entry: ReviewLogEntryDetail;
  onClose: () => void;
}

const ReviewLogPopup: React.FC<ReviewLogPopupProps> = ({ entry, onClose }) => {
  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/60 backdrop-blur-sm"
        onClick={onClose}
      />
      {/* Popup Content */}
      <div
        className="relative w-[500px] max-h-[80vh] rounded-lg border border-border-subtle shadow-2xl overflow-hidden flex flex-col"
        style={{ backgroundColor: 'rgba(20, 24, 32, 0.98)' }}
      >
        {/* Header */}
        <div className="flex-shrink-0 flex items-center justify-between p-4 border-b border-border-subtle">
          <div className="flex items-center gap-3">
            <span className={cn(
              "w-6 h-6 rounded-full flex items-center justify-center text-sm font-bold",
              entry.result === 'Passed'
                ? "bg-neon-green/20 text-neon-green"
                : entry.result === 'Failed'
                ? "bg-neon-red/20 text-neon-red"
                : "bg-neon-orange/20 text-neon-orange"
            )}>
              {entry.result === 'Passed' ? '✓' : entry.result === 'Failed' ? '✗' : '○'}
            </span>
            <h3 className="font-semibold text-text-primary">Review Detail</h3>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 rounded hover:bg-surface-elevated transition-colors"
          >
            <X className="w-4 h-4 text-text-muted" />
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-4 space-y-4">
          {/* Node Info */}
          <div>
            <span className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">
              Knowledge Node
            </span>
            <p className="text-text-primary font-mono text-sm break-words">
              {entry.nodeLabel}
            </p>
            <p className="text-text-muted text-xs font-mono mt-1 break-all">
              {entry.nodeId}
            </p>
          </div>

          {/* Result */}
          <div>
            <span className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">
              Review Result
            </span>
            <span className={cn(
              "inline-flex items-center gap-2 px-3 py-1.5 rounded-full text-sm font-semibold",
              entry.result === 'Passed'
                ? "bg-neon-green/20 text-neon-green"
                : entry.result === 'Failed'
                ? "bg-neon-red/20 text-neon-red"
                : "bg-neon-orange/20 text-neon-orange"
            )}>
              {entry.result === 'Passed' ? '✓ Passed' : entry.result === 'Failed' ? '✗ Failed' : '○ Skipped'}
            </span>
          </div>

          {/* Timestamp */}
          <div>
            <span className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">
              Reviewed At
            </span>
            <p className="text-text-primary font-mono text-sm">
              {new Date(entry.timestamp).toLocaleString()}
            </p>
          </div>

          {/* Deactivated Reason (if Failed and reason provided) */}
          {entry.result === 'Failed' && entry.deactivatedReason && (
            <div>
              <span className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">
                Deactivation Reason
              </span>
              <div className="p-3 rounded-lg bg-neon-red/10 border border-neon-red/30">
                <p className="text-neon-red text-sm whitespace-pre-wrap">
                  {entry.deactivatedReason}
                </p>
              </div>
            </div>
          )}

          {/* LLM Review Response (verification content from coordinator) */}
          {entry.verificationContent && (
            <div>
              <span className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">
                {entry.result === 'Failed' ? 'Failure Reason (LLM Response)' : 'LLM Review Response'}
              </span>
              <div className={cn(
                "p-3 rounded-lg border max-h-[280px] overflow-y-auto",
                entry.result === 'Failed'
                  ? "bg-neon-red/5 border-neon-red/30"
                  : "bg-surface-elevated border-border-subtle"
              )}>
                <pre className={cn(
                  "text-xs font-mono whitespace-pre-wrap break-words",
                  entry.result === 'Failed' ? "text-neon-red/90" : "text-text-primary"
                )}>
                  {entry.verificationContent}
                </pre>
              </div>
            </div>
          )}

          {/* No details message - only show if no verification content */}
          {!entry.verificationContent && (
            <div className="p-3 rounded-lg bg-surface-elevated border border-border-subtle">
              <p className="text-text-muted text-sm text-center">
                No LLM response captured for this review.
              </p>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex-shrink-0 p-4 border-t border-border-subtle">
          <button
            onClick={onClose}
            className="w-full px-4 py-2 rounded-lg bg-surface-elevated hover:opacity-80 border border-border-subtle transition-colors text-sm"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
};

// Review Log Table Popup Component with Pagination and Detail Panel
interface ReviewLogTablePopupProps {
  entries: ReviewLogEntryDetail[];
  onClose: () => void;
}

const ROWS_PER_PAGE = 10;
const TABLE_HEIGHT = 440; // Fixed height for 10 rows (44px each)

// Helper to extract failure reason from LLM JSON output
function extractFailureReason(verificationContent: string | null | undefined): string | null {
  if (!verificationContent) return null;

  try {
    // Try to find and parse JSON objects in the content
    // The LLM output might have multiple JSON objects, we want the one with "reason"
    const jsonMatches = verificationContent.match(/\{[^{}]*"reason"\s*:\s*"[^"]*"[^{}]*\}/g);
    if (jsonMatches) {
      for (const match of jsonMatches) {
        try {
          const parsed = JSON.parse(match);
          if (parsed.reason && typeof parsed.reason === 'string') {
            return parsed.reason;
          }
        } catch {
          // Continue to next match
        }
      }
    }

    // Try to extract reason from a larger JSON structure
    const reasonMatch = verificationContent.match(/"reason"\s*:\s*"([^"]+)"/);
    if (reasonMatch && reasonMatch[1]) {
      return reasonMatch[1];
    }
  } catch {
    // Fall through to return truncated content
  }

  // If we can't parse, return first meaningful text (skip json markers)
  const cleanedContent = verificationContent
    .replace(/^[\s`]*json[\s`]*/i, '')
    .replace(/```/g, '')
    .trim();

  if (cleanedContent.length > 150) {
    return cleanedContent.slice(0, 150) + '...';
  }
  return cleanedContent || null;
}

// Helper to download entries as CSV
function downloadAsCSV(entries: ReviewLogEntryDetail[]) {
  const headers = ['Node ID', 'Knowledge Node', 'Explain Content', 'Result', 'Reviewed At', 'Failure Reason', 'Verification Content'];

  const escapeCSV = (str: string | null | undefined) => {
    if (!str) return '';
    // Escape quotes and wrap in quotes if contains comma, quote, or newline
    const escaped = str.replace(/"/g, '""');
    if (escaped.includes(',') || escaped.includes('"') || escaped.includes('\n')) {
      return `"${escaped}"`;
    }
    return escaped;
  };

  const rows = entries.map(entry => {
    const failureReason = entry.result === 'Failed'
      ? (entry.deactivatedReason || extractFailureReason(entry.verificationContent))
      : null;

    return [
      escapeCSV(entry.nodeId),
      escapeCSV(entry.nodeLabel),
      escapeCSV(entry.explainContent),
      escapeCSV(entry.result),
      escapeCSV(new Date(entry.timestamp).toISOString()),
      escapeCSV(failureReason),
      escapeCSV(entry.verificationContent),
    ].join(',');
  });

  const csv = [headers.join(','), ...rows].join('\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `review-log-${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.csv`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

const ReviewLogTablePopup: React.FC<ReviewLogTablePopupProps> = ({ entries, onClose }) => {
  const [currentPage, setCurrentPage] = useState(1);
  const [selectedEntry, setSelectedEntry] = useState<ReviewLogEntryDetail | null>(null);

  const totalPages = Math.max(1, Math.ceil(entries.length / ROWS_PER_PAGE));
  const startIndex = (currentPage - 1) * ROWS_PER_PAGE;
  const paginatedEntries = entries.slice(startIndex, startIndex + ROWS_PER_PAGE);

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/70 backdrop-blur-sm"
        onClick={onClose}
      />
      {/* Popup Content - flex row for table + detail panel */}
      <div
        className="relative w-[95vw] max-w-[1400px] h-[700px] rounded-lg border border-border-subtle shadow-2xl overflow-hidden flex"
        style={{ backgroundColor: 'rgba(20, 24, 32, 0.98)' }}
      >
        {/* Left: Table Section */}
        <div className={cn(
          "flex flex-col transition-all duration-300",
          selectedEntry ? "w-[60%]" : "w-full"
        )}>
          {/* Header */}
          <div className="flex-shrink-0 flex items-center justify-between p-4 border-b border-border-subtle">
            <div className="flex items-center gap-3">
              <Table2 className="w-5 h-5 text-neon-cyan" />
              <h3 className="font-semibold text-text-primary">Review Log - Current Iteration</h3>
              <span className="text-xs text-text-muted px-2 py-0.5 rounded-full bg-surface-elevated">
                {entries.length} entries
              </span>
            </div>
            <div className="flex items-center gap-2">
              {/* CSV Download Button */}
              <button
                onClick={() => downloadAsCSV(entries)}
                disabled={entries.length === 0}
                className={cn(
                  "flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors",
                  entries.length === 0
                    ? "bg-surface-elevated/50 text-text-muted/50 cursor-not-allowed"
                    : "bg-neon-cyan/10 text-neon-cyan hover:bg-neon-cyan/20 border border-neon-cyan/30"
                )}
                title="Download as CSV"
              >
                <Download className="w-3.5 h-3.5" />
                <span>Export CSV</span>
              </button>
              <button
                onClick={onClose}
                className="p-1.5 rounded hover:bg-surface-elevated transition-colors"
              >
                <X className="w-4 h-4 text-text-muted" />
              </button>
            </div>
          </div>

          {/* Table - Modern Design */}
          <div className="flex-shrink-0 px-4 pb-3">
            <div className="overflow-auto rounded-lg border border-border-subtle/60" style={{ maxHeight: TABLE_HEIGHT }}>
              <table className="w-full text-sm border-collapse">
                <thead className="sticky top-0 z-10">
                  <tr
                    className="text-left text-text-dimmed text-xs uppercase tracking-wider"
                    style={{ backgroundColor: 'rgba(30, 35, 45, 0.98)' }}
                  >
                    <th className="px-4 py-3 font-semibold w-[150px] border-b-2 border-neon-cyan/30">
                      <div className="flex items-center gap-2">
                        <span className="w-1.5 h-1.5 rounded-full bg-neon-cyan/60"></span>
                        Node ID
                      </div>
                    </th>
                    <th className="px-4 py-3 font-semibold border-l border-border-subtle/40 border-b-2 border-b-neon-cyan/30">
                      <div className="flex items-center gap-2">
                        <span className="w-1.5 h-1.5 rounded-full bg-neon-purple/60"></span>
                        Knowledge Node
                      </div>
                    </th>
                    <th className="px-4 py-3 font-semibold text-center w-[100px] border-l border-border-subtle/40 border-b-2 border-b-neon-cyan/30">
                      <div className="flex items-center justify-center gap-2">
                        <span className="w-1.5 h-1.5 rounded-full bg-neon-green/60"></span>
                        Result
                      </div>
                    </th>
                    <th className="px-4 py-3 font-semibold w-[170px] border-l border-border-subtle/40 border-b-2 border-b-neon-cyan/30">
                      <div className="flex items-center gap-2">
                        <span className="w-1.5 h-1.5 rounded-full bg-neon-gold/60"></span>
                        Reviewed At
                      </div>
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border-subtle/30">
                  {entries.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="text-center py-16 text-text-muted">
                        <div className="flex flex-col items-center gap-2">
                          <div className="w-12 h-12 rounded-full bg-surface-elevated flex items-center justify-center">
                            <Table2 className="w-6 h-6 text-text-dimmed" />
                          </div>
                          <span>No review logs in current iteration</span>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    paginatedEntries.map((entry, idx) => {
                      const isSelected = selectedEntry?.nodeId === entry.nodeId && selectedEntry?.timestamp === entry.timestamp;
                      const isEven = idx % 2 === 0;

                      return (
                        <tr
                          key={`${entry.nodeId}-${idx}`}
                          onClick={() => setSelectedEntry(entry)}
                          className={cn(
                            "h-[44px] cursor-pointer transition-all duration-200 group",
                            isSelected
                              ? "bg-neon-cyan/15 shadow-[inset_3px_0_0_0_rgba(0,255,255,0.8)]"
                              : isEven
                                ? "bg-transparent hover:bg-neon-cyan/5"
                                : "bg-surface-elevated/20 hover:bg-neon-cyan/5",
                            "hover:shadow-[inset_0_0_20px_rgba(0,255,255,0.05)]"
                          )}
                        >
                          <td className="px-4 py-2">
                            <div
                              className={cn(
                                "font-mono text-xs truncate transition-colors duration-200",
                                isSelected ? "text-neon-cyan" : "text-text-primary group-hover:text-neon-cyan"
                              )}
                              title={entry.nodeId}
                            >
                              {entry.nodeId.length > 18 ? `${entry.nodeId.slice(0, 10)}...${entry.nodeId.slice(-6)}` : entry.nodeId}
                            </div>
                          </td>
                          <td className="px-4 py-2 border-l border-border-subtle/30">
                            <div
                              className={cn(
                                "truncate text-sm transition-colors duration-200",
                                isSelected ? "text-text-primary" : "text-text-primary/90 group-hover:text-text-primary"
                              )}
                              title={entry.nodeLabel}
                            >
                              {entry.nodeLabel || '-'}
                            </div>
                          </td>
                          <td className="px-4 py-2 text-center border-l border-border-subtle/30">
                            <span className={cn(
                              "inline-flex items-center justify-center w-7 h-7 rounded-lg text-sm font-bold transition-all duration-200",
                              entry.result === 'Passed'
                                ? "bg-neon-green/20 text-neon-green group-hover:bg-neon-green/30 group-hover:shadow-[0_0_10px_rgba(0,255,100,0.3)]"
                                : entry.result === 'Failed'
                                ? "bg-neon-red/20 text-neon-red group-hover:bg-neon-red/30 group-hover:shadow-[0_0_10px_rgba(255,50,50,0.3)]"
                                : "bg-neon-orange/20 text-neon-orange group-hover:bg-neon-orange/30"
                            )}>
                              {entry.result === 'Passed' ? '✓' : entry.result === 'Failed' ? '✗' : '○'}
                            </span>
                          </td>
                          <td className="px-4 py-2 border-l border-border-subtle/30">
                            <span className={cn(
                              "text-xs font-mono whitespace-nowrap transition-colors duration-200",
                              isSelected ? "text-text-muted" : "text-text-muted/80 group-hover:text-text-muted"
                            )}>
                              {new Date(entry.timestamp).toLocaleString()}
                            </span>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {/* Pagination Footer - always visible */}
          <div className="flex-shrink-0 flex items-center justify-between px-4 py-3 border-t border-border-subtle">
            <span className="text-text-muted text-sm">
              {entries.length === 0
                ? 'No entries'
                : `Showing ${startIndex + 1}-${Math.min(startIndex + ROWS_PER_PAGE, entries.length)} of ${entries.length}`
              }
            </span>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
                disabled={currentPage === 1}
                className={cn(
                  "p-1.5 rounded transition-colors",
                  currentPage === 1
                    ? "text-text-muted/50 cursor-not-allowed"
                    : "text-text-muted hover:bg-surface-elevated hover:text-text-primary"
                )}
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <div className="flex items-center gap-1">
                {Array.from({ length: totalPages }, (_, i) => i + 1).slice(0, 7).map(page => (
                  <button
                    key={page}
                    onClick={() => setCurrentPage(page)}
                    className={cn(
                      "w-8 h-8 rounded text-sm font-medium transition-colors",
                      page === currentPage
                        ? "bg-neon-cyan/20 text-neon-cyan"
                        : "text-text-muted hover:bg-surface-elevated hover:text-text-primary"
                    )}
                  >
                    {page}
                  </button>
                ))}
                {totalPages > 7 && (
                  <span className="text-text-muted px-1">...</span>
                )}
              </div>
              <button
                onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
                disabled={currentPage === totalPages}
                className={cn(
                  "p-1.5 rounded transition-colors",
                  currentPage === totalPages
                    ? "text-text-muted/50 cursor-not-allowed"
                    : "text-text-muted hover:bg-surface-elevated hover:text-text-primary"
                )}
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        </div>

        {/* Right: Detail Panel */}
        {selectedEntry && (
          <div className="w-[40%] border-l border-border-subtle flex flex-col overflow-hidden">
            {/* Detail Header */}
            <div className="flex-shrink-0 flex items-center justify-between p-4 border-b border-border-subtle">
              <h4 className="font-semibold text-text-primary">Review Detail</h4>
              <button
                onClick={() => setSelectedEntry(null)}
                className="p-1 rounded hover:bg-surface-elevated transition-colors"
              >
                <X className="w-4 h-4 text-text-muted" />
              </button>
            </div>

            {/* Detail Content - scrollable */}
            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              {/* Result Badge */}
              <div className="flex items-center gap-3">
                <span className={cn(
                  "inline-flex items-center gap-2 px-3 py-1.5 rounded-full text-sm font-semibold",
                  selectedEntry.result === 'Passed'
                    ? "bg-neon-green/20 text-neon-green"
                    : selectedEntry.result === 'Failed'
                    ? "bg-neon-red/20 text-neon-red"
                    : "bg-neon-orange/20 text-neon-orange"
                )}>
                  {selectedEntry.result === 'Passed' ? '✓ Passed' : selectedEntry.result === 'Failed' ? '✗ Failed' : '○ Skipped'}
                </span>
                <span className="text-text-muted text-xs font-mono">
                  {new Date(selectedEntry.timestamp).toLocaleString()}
                </span>
              </div>

              {/* Node ID */}
              <div>
                <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">Node ID</label>
                <div className="h-[36px] p-2 rounded bg-surface-elevated border border-border-subtle overflow-x-auto">
                  <code className="text-text-primary text-xs font-mono whitespace-nowrap">{selectedEntry.nodeId}</code>
                </div>
              </div>

              {/* Knowledge Node */}
              <div>
                <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">Knowledge Node</label>
                <div className="h-[60px] p-2 rounded bg-surface-elevated border border-border-subtle overflow-y-auto">
                  <p className="text-text-primary text-sm whitespace-pre-wrap break-words">{selectedEntry.nodeLabel || '-'}</p>
                </div>
              </div>

              {/* Explain Content */}
              <div>
                <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">Explain Content</label>
                <div className="h-[120px] p-2 rounded bg-surface-elevated border border-border-subtle overflow-y-auto">
                  <p className="text-text-muted text-xs whitespace-pre-wrap break-words font-mono">
                    {selectedEntry.explainContent || '-'}
                  </p>
                </div>
              </div>

              {/* Failure Reason (if failed) */}
              {selectedEntry.result === 'Failed' && (
                <div>
                  <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">Failure Reason</label>
                  <div className="h-[80px] p-2 rounded bg-neon-red/10 border border-neon-red/30 overflow-y-auto">
                    <p className="text-neon-red text-sm whitespace-pre-wrap break-words">
                      {selectedEntry.deactivatedReason || extractFailureReason(selectedEntry.verificationContent) || '-'}
                    </p>
                  </div>
                </div>
              )}

              {/* Verification Content (LLM Response) */}
              {selectedEntry.verificationContent && (
                <div>
                  <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">LLM Response</label>
                  <div className="h-[160px] p-2 rounded bg-surface-elevated border border-border-subtle overflow-y-auto">
                    <pre className="text-text-primary text-xs font-mono whitespace-pre-wrap break-words">
                      {selectedEntry.verificationContent}
                    </pre>
                  </div>
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

type Tab = 'status' | 'history' | 'settings';

const ReviewAgentDashboardInner: React.FC<ReviewAgentDashboardProps> = ({ isOpen, onClose }) => {
  const [activeTab, setActiveTab] = useState<Tab>('status');
  const [selectedIterationId, setSelectedIterationId] = useState<string | null>(null);
  const [isAgentPanelExpanded, setIsAgentPanelExpanded] = useState(false);
  const [selectedLogEntry, setSelectedLogEntry] = useState<ReviewLogEntryDetail | null>(null);
  const [isReviewLogTableOpen, setIsReviewLogTableOpen] = useState(false);
  const [isTriggering, setIsTriggering] = useState(false);

  const {
    isConnected,
    status,
    currentNodeId,
    currentNodeLabel,
    agentCards,
    reviewLog,
    nodesReviewed,
    nodesPending,
    nodesDeactivated,
    nodesRemoved,
    iterationStartTime,
    isRunning,
    hasStarted,
    refreshStatus,
    triggerReview,
    reconnect,
  } = useReviewAgent({ enabled: isOpen });

  const isWorking = status?.status === 'WorkingReviewRound' || status?.status === 'WorkingCleanupRound';
  const hasAgentActivity = isWorking && agentCards.length > 0;

  const handleTriggerReview = async () => {
    setIsTriggering(true);
    try {
      const result = await triggerReview();
      console.log('[ReviewAgentDashboard] Trigger result:', result);
    } finally {
      setIsTriggering(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-end pt-20 pr-4">
      {/* Backdrop */}
      <div
        className="absolute inset-0 backdrop-blur-sm"
        style={{ backgroundColor: 'rgba(12, 15, 20, 0.5)' }}
        onClick={onClose}
      />

      {/* Container for both panels */}
      <div className="relative flex items-stretch gap-3">
        {/* Agent Cards Side Panel (Left) - Collapsible, fixed size */}
        <div className={cn(
          "overflow-hidden transition-all duration-300 ease-in-out",
          isAgentPanelExpanded ? "w-[1008px] opacity-100 scale-100" : "w-0 opacity-0 scale-95"
        )}>
          <div
            className="h-full max-h-[calc(100vh-6rem)] flex flex-col rounded-lg border border-border-subtle shadow-xl"
            style={{ backgroundColor: 'rgba(20, 24, 32, 0.95)' }}
          >
            {/* Side Panel Header */}
            <div className="flex-shrink-0 p-3 border-b border-border-subtle" style={{ backgroundColor: 'rgba(20, 24, 32, 0.95)' }}>
              <div className="flex items-center gap-2">
                <Bot className="w-4 h-4 text-neon-purple" />
                <h3 className="text-sm font-semibold text-neon-purple">
                  LLM Activity
                </h3>
                <span className="text-xs text-text-muted">
                  ({agentCards.length} agent{agentCards.length !== 1 ? 's' : ''})
                </span>
              </div>
            </div>

            {/* Agent Cards - 2 rows x 3 columns grid, fixed size cards */}
            <div className="flex-1 p-3 overflow-auto">
              {agentCards.length === 0 ? (
                <div className="h-full flex items-center justify-center text-text-muted text-sm">
                  No agent activity yet
                </div>
              ) : (
                <div className="grid grid-cols-3 gap-3">
                  {/* Sort: coordinator first, then workers */}
                  {(() => {
                    const sorted = [...agentCards].sort((a, b) => {
                      if (a.agentRole === 'coordinator') return -1;
                      if (b.agentRole === 'coordinator') return 1;
                      return 0;
                    });
                    // Take up to 6 cards (2x3 grid), fixed 320x340px each
                    return sorted.slice(0, 6).map((card, index) => (
                      <TokenCard
                        key={card.agentId}
                        agentId={card.agentId}
                        agentRole={card.agentRole}
                        tokens={card.tokens}
                        isComplete={card.isComplete}
                        index={index}
                      />
                    ));
                  })()}
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Toggle Button - Cyberpunk neon style */}
        <button
          onClick={() => setIsAgentPanelExpanded(!isAgentPanelExpanded)}
          className={cn(
            "relative flex-shrink-0 self-center w-7 h-20 rounded-full flex items-center justify-center",
            "border-2 transition-all duration-300 ease-out",
            "hover:scale-110 active:scale-95",
            hasAgentActivity
              ? "bg-gradient-to-b from-neon-cyan via-neon-purple to-neon-pink border-neon-cyan shadow-[0_0_15px_rgba(0,255,255,0.5),0_0_30px_rgba(0,255,255,0.3)] hover:shadow-[0_0_20px_rgba(0,255,255,0.7),0_0_40px_rgba(0,255,255,0.4)]"
              : "bg-gradient-to-b from-gray-700 to-gray-800 border-gray-600 hover:border-neon-cyan/50 hover:shadow-[0_0_10px_rgba(0,255,255,0.3)]"
          )}
          title={isAgentPanelExpanded ? "Collapse LLM Activity" : "Expand LLM Activity"}
        >
          <div className={cn(
            "transition-transform duration-300",
            isAgentPanelExpanded ? "rotate-180" : "rotate-0"
          )}>
            <ChevronLeft className={cn(
              "w-4 h-4 drop-shadow-[0_0_3px_rgba(0,255,255,0.8)]",
              hasAgentActivity ? "text-white" : "text-gray-400"
            )} />
          </div>
          {hasAgentActivity && !isAgentPanelExpanded && (
            <span className="absolute top-1 right-0.5 w-2 h-2 bg-neon-green rounded-full animate-pulse shadow-[0_0_8px_rgba(0,255,0,0.8)]" />
          )}
        </button>

        {/* Main Panel (Right) */}
        <div className={cn(
          "relative w-[420px] max-h-[calc(100vh-6rem)] flex flex-col",
          "backdrop-blur-xl border border-border-subtle rounded-lg",
          "shadow-2xl",
          "transform transition-all duration-300",
          isOpen ? "translate-x-0 opacity-100" : "translate-x-full opacity-0"
        )}
        style={{ backgroundColor: 'rgba(20, 24, 32, 0.95)' }}>
        {/* Header */}
        <div className="flex-shrink-0 flex items-center justify-between p-4 border-b border-border-subtle" style={{ backgroundColor: 'rgba(20, 24, 32, 0.95)' }}>
          <div className="flex items-center gap-2">
            <div className={cn(
              "w-2 h-2 rounded-full",
              isConnected ? "bg-neon-green animate-pulse" : "bg-neon-red"
            )} />
            <h2 className="font-display text-lg font-semibold text-neon-cyan">
              Review Agent
            </h2>
            {isConnected ? (
              <Wifi className="w-3 h-3 text-neon-green" />
            ) : (
              <button
                onClick={reconnect}
                className="p-1 hover:bg-surface-elevated rounded transition-colors"
                title="Click to reconnect"
              >
                <WifiOff className="w-3 h-3 text-neon-red" />
              </button>
            )}
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={refreshStatus}
              className="p-2 rounded-lg hover:bg-surface-elevated transition-colors"
              aria-label="Refresh"
            >
              <RefreshCw className="w-4 h-4 text-text-muted" />
            </button>
            <button
              onClick={onClose}
              className="p-2 rounded-lg hover:bg-surface-elevated transition-colors"
              aria-label="Close"
            >
              <X className="w-4 h-4 text-text-muted" />
            </button>
          </div>
        </div>

        {/* Tabs */}
        <div className="flex-shrink-0 flex border-b border-border-subtle">
          <button
            onClick={() => {
              setActiveTab('status');
              setSelectedIterationId(null);
            }}
            className={cn(
              "flex-1 flex items-center justify-center gap-2 px-4 py-3 text-sm font-medium transition-colors",
              activeTab === 'status'
                ? "text-neon-cyan border-b-2 border-neon-cyan"
                : "text-text-muted hover:text-text-primary"
            )}
          >
            <Activity className="w-4 h-4" />
            Status
          </button>
          <button
            onClick={() => {
              setActiveTab('history');
              setSelectedIterationId(null);
            }}
            className={cn(
              "flex-1 flex items-center justify-center gap-2 px-4 py-3 text-sm font-medium transition-colors",
              activeTab === 'history'
                ? "text-neon-cyan border-b-2 border-neon-cyan"
                : "text-text-muted hover:text-text-primary"
            )}
          >
            <History className="w-4 h-4" />
            History
          </button>
          <button
            onClick={() => {
              setActiveTab('settings');
              setSelectedIterationId(null);
            }}
            className={cn(
              "flex-1 flex items-center justify-center gap-2 px-4 py-3 text-sm font-medium transition-colors",
              activeTab === 'settings'
                ? "text-neon-cyan border-b-2 border-neon-cyan"
                : "text-text-muted hover:text-text-primary"
            )}
          >
            <Settings className="w-4 h-4" />
            Settings
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-auto p-4">
          {activeTab === 'status' && (
            <div className="space-y-6">
              {!isConnected && (
                <div className="p-3 rounded-lg bg-neon-orange/10 border border-neon-orange/30">
                  <p className="text-neon-orange text-sm">
                    Connecting to real-time updates...
                  </p>
                </div>
              )}

              {status?.errorMessage && (
                <div className="p-3 rounded-lg bg-neon-red/10 border border-neon-red/30">
                  <p className="text-neon-red text-sm">{status.errorMessage}</p>
                </div>
              )}

              {!status && isConnected && (
                <div className="flex items-center justify-center py-8">
                  <div className="w-8 h-8 border-2 border-neon-cyan border-t-transparent rounded-full animate-spin" />
                </div>
              )}

              {status && (
                <>
                  <ReviewAgentStatus
                    status={status.status}
                    currentIterationId={status.currentIterationId}
                    lastCompletedAt={status.lastCompletedAt}
                    nextScheduledAt={status.nextScheduledAt}
                    errorMessage={status.errorMessage}
                  />

                  {/* Current Node Progress (real-time) - always visible, fixed size with scroll */}
                  <div className="border-t border-border-subtle pt-4">
                    <h3 className="text-text-dimmed text-xs uppercase tracking-wider font-semibold mb-3">
                      Currently Reviewing
                    </h3>
                    <div className="h-[72px] p-3 rounded-lg bg-surface-elevated border border-border-subtle overflow-y-auto">
                      {isWorking && currentNodeLabel ? (
                        <>
                          <p className="text-text-primary font-mono text-sm break-words">
                            {currentNodeLabel}
                          </p>
                          <p className="text-text-muted text-xs mt-1 font-mono break-all">
                            {currentNodeId}
                          </p>
                        </>
                      ) : (
                        <p className="text-text-muted font-mono text-sm">-</p>
                      )}
                    </div>
                  </div>

                  {/* Cleanup Round Indicator (T080) */}
                  {status?.status === 'WorkingCleanupRound' && (
                    <div className="border-t border-border-subtle pt-4">
                      <h3 className="text-text-dimmed text-xs uppercase tracking-wider font-semibold mb-3">
                        Cleanup Round
                      </h3>
                      <div className="p-3 rounded-lg bg-neon-orange/10 border border-neon-orange/30">
                        <div className="flex items-center gap-2 mb-2">
                          <div className="w-2 h-2 rounded-full bg-neon-orange animate-pulse" />
                          <span className="text-neon-orange text-sm font-semibold">
                            Removing deactivated nodes...
                          </span>
                        </div>
                        <p className="text-text-muted text-xs">
                          Cleaning up nodes that have been deactivated beyond the delete threshold.
                        </p>
                        {nodesRemoved > 0 && (
                          <p className="text-neon-red text-sm font-semibold mt-2">
                            {nodesRemoved} node{nodesRemoved !== 1 ? 's' : ''} removed
                          </p>
                        )}
                      </div>
                    </div>
                  )}

                  <div className="border-t border-border-subtle pt-4">
                    <ReviewAgentProgress
                      nodesReviewed={nodesReviewed}
                      nodesPending={nodesPending}
                      nodesDeactivated={nodesDeactivated}
                      nodesRemoved={nodesRemoved}
                      iterationStartTime={iterationStartTime}
                      currentNodeId={currentNodeId}
                      reviewLog={reviewLog}
                    />
                  </div>

                  {/* Review Log - real-time results with click to view details */}
                  <div className="border-t border-border-subtle pt-4">
                    <div className="flex items-center justify-between mb-3">
                      <h3 className="text-text-dimmed text-xs uppercase tracking-wider font-semibold">
                        Review Log
                        <span className="ml-2 text-text-muted text-[10px] normal-case tracking-normal font-normal">
                          (click for details)
                        </span>
                      </h3>
                      <button
                        onClick={() => setIsReviewLogTableOpen(true)}
                        className="flex items-center gap-1.5 px-2 py-1 rounded text-xs text-neon-cyan hover:bg-neon-cyan/10 transition-colors"
                        title="View all in table"
                      >
                        <Table2 className="w-3.5 h-3.5" />
                        <span>Table View</span>
                      </button>
                    </div>
                    <div className="h-[160px] rounded-lg bg-surface-elevated border border-border-subtle overflow-y-auto">
                      {reviewLog.length === 0 ? (
                        <div className="h-full flex items-center justify-center text-text-muted text-sm">
                          -
                        </div>
                      ) : (
                        <div className="divide-y divide-border-subtle">
                          {reviewLog.map((entry, idx) => (
                            <button
                              key={`${entry.nodeId}-${idx}`}
                              onClick={() => setSelectedLogEntry(entry)}
                              className="w-full px-3 py-2 flex items-start gap-2 hover:bg-surface-elevated/80 transition-colors text-left"
                            >
                              <span className={cn(
                                "flex-shrink-0 w-5 h-5 rounded-full flex items-center justify-center text-xs font-bold",
                                entry.result === 'Passed'
                                  ? "bg-neon-green/20 text-neon-green"
                                  : entry.result === 'Failed'
                                  ? "bg-neon-red/20 text-neon-red"
                                  : "bg-neon-orange/20 text-neon-orange"
                              )}>
                                {entry.result === 'Passed' ? '✓' : entry.result === 'Failed' ? '✗' : '○'}
                              </span>
                              <div className="flex-1 min-w-0">
                                <p className="text-text-primary text-xs font-mono truncate" title={entry.nodeLabel}>
                                  {entry.nodeLabel}
                                </p>
                                <p className="text-text-muted text-[10px] font-mono">
                                  {new Date(entry.timestamp).toLocaleTimeString()}
                                </p>
                              </div>
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>

                  {/* Quick Actions */}
                  <div className="border-t border-border-subtle pt-4">
                    <h3 className="text-text-dimmed text-xs uppercase tracking-wider font-semibold mb-3">
                      Quick Actions
                    </h3>
                    <div className="flex gap-2">
                      {/* Start Review Button - shown when not started or idle */}
                      <button
                        onClick={handleTriggerReview}
                        disabled={isRunning || isTriggering}
                        className={cn(
                          "flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-lg border transition-colors text-sm font-medium",
                          isRunning || isTriggering
                            ? "bg-surface-elevated/50 border-border-subtle text-text-muted cursor-not-allowed"
                            : hasStarted
                            ? "bg-neon-cyan/10 border-neon-cyan/30 text-neon-cyan hover:bg-neon-cyan/20"
                            : "bg-neon-green/10 border-neon-green/30 text-neon-green hover:bg-neon-green/20"
                        )}
                      >
                        {isTriggering ? (
                          <>
                            <Loader2 className="w-4 h-4 animate-spin" />
                            <span>Triggering...</span>
                          </>
                        ) : isRunning ? (
                          <>
                            <Loader2 className="w-4 h-4 animate-spin" />
                            <span>Running...</span>
                          </>
                        ) : (
                          <>
                            <Play className="w-4 h-4" />
                            <span>{hasStarted ? 'Start Review' : 'Start First Review'}</span>
                          </>
                        )}
                      </button>
                      <button
                        onClick={() => setActiveTab('settings')}
                        className="flex items-center justify-center gap-2 px-3 py-2 rounded-lg bg-surface-elevated hover:opacity-80 border border-border-subtle transition-colors text-sm"
                      >
                        <Settings className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                </>
              )}
            </div>
          )}

          {activeTab === 'history' && (
            <div>
              {selectedIterationId ? (
                <ReviewAgentIterationDetail
                  iterationId={selectedIterationId}
                  onBack={() => setSelectedIterationId(null)}
                />
              ) : (
                <ReviewAgentHistory
                  onSelectIteration={(id) => setSelectedIterationId(id)}
                />
              )}
            </div>
          )}

          {activeTab === 'settings' && (
            <ReviewAgentSettingsPanel />
          )}
        </div>
      </div>
      </div>

      {/* Review Log Detail Popup */}
      {selectedLogEntry && (
        <ReviewLogPopup
          entry={selectedLogEntry}
          onClose={() => setSelectedLogEntry(null)}
        />
      )}

      {/* Review Log Table Popup */}
      {isReviewLogTableOpen && (
        <ReviewLogTablePopup
          entries={reviewLog}
          onClose={() => setIsReviewLogTableOpen(false)}
        />
      )}
    </div>
  );
};

// Wrapper component with Error Boundary
const ReviewAgentDashboard: React.FC<ReviewAgentDashboardProps> = (props) => {
  if (!props.isOpen) return null;

  return (
    <ReviewAgentErrorBoundary onClose={props.onClose}>
      <ReviewAgentDashboardInner {...props} />
    </ReviewAgentErrorBoundary>
  );
};

export default ReviewAgentDashboard;
