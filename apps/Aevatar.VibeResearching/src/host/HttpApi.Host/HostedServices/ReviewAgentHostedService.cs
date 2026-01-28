using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.VibeResearching.Agents.ReviewAgent;
using Aevatar.VibeResearching.HttpApi.Host;

namespace Aevatar.VibeResearching.HttpApi.Host.HostedServices;

using IReviewAgentEventPublisher = Aevatar.VibeResearching.Agents.ReviewAgent.IReviewAgentEventPublisher;
using IReviewAgentStorage = Aevatar.VibeResearching.Agents.ReviewAgent.IReviewAgentStorage;

/// <summary>
/// Background service that runs the Review Agent on a schedule.
/// Requires manual trigger for the first round, then auto-schedules subsequent rounds.
/// Publishes SSE events for real-time frontend updates.
/// </summary>
public sealed class ReviewAgentHostedService : BackgroundService, IReviewAgentTrigger
{
    private readonly ReviewAgentService _reviewAgentService;
    private readonly IReviewAgentEventPublisher _eventPublisher;
    private readonly IReviewAgentStorage _storage;
    private readonly IOptionsMonitor<ReviewAgentOptions> _optionsMonitor;
    private readonly ILogger<ReviewAgentHostedService> _logger;

    // Manual trigger state
    private volatile bool _hasStarted;
    private volatile bool _isRunning;
    private readonly SemaphoreSlim _triggerSemaphore = new(1, 1);
    private TaskCompletionSource? _triggerSignal;
    private CancellationToken _stoppingToken;

    public ReviewAgentHostedService(
        IReviewAgentService reviewAgentService,
        IReviewAgentEventPublisher eventPublisher,
        IReviewAgentStorage storage,
        IOptionsMonitor<ReviewAgentOptions> optionsMonitor,
        ILogger<ReviewAgentHostedService> logger)
    {
        // Cast to concrete type to access event handlers
        _reviewAgentService = (Aevatar.VibeResearching.Agents.ReviewAgent.ReviewAgentService)(reviewAgentService ?? throw new ArgumentNullException(nameof(reviewAgentService)));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Wire up event handlers for SSE
        _reviewAgentService.OnNodeProgress = async (nodeId, nodeLabel, explainContent, nodesReviewed, nodesPending, nodesDeactivated, result) =>
        {
            await _eventPublisher.PublishNodeProgressAsync(
                nodeId,
                nodeLabel,
                explainContent,
                nodesReviewed,
                nodesPending,
                nodesDeactivated,
                result);
        };

        _reviewAgentService.OnTokenStream = async (agentId, agentRole, token, isComplete) =>
        {
            await _eventPublisher.PublishTokenAsync(agentId, agentRole, token, isComplete);
        };
    }

    // IReviewAgentTrigger implementation
    public bool IsRunning => _isRunning;
    public bool HasStarted => _hasStarted;

    public async Task<bool> TriggerReviewRoundAsync()
    {
        if (!await _triggerSemaphore.WaitAsync(0))
        {
            // Already running or being triggered
            return false;
        }

        try
        {
            if (_isRunning)
            {
                return false;
            }

            // Signal the waiting loop to start a review round
            _triggerSignal?.TrySetResult();
            return true;
        }
        finally
        {
            _triggerSemaphore.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        _logger.LogInformation("Review Agent hosted service starting (manual trigger required for first round)...");

        // Load persisted config from storage if available (T065)
        try
        {
            var savedConfig = await _storage.LoadConfigAsync(stoppingToken);
            if (savedConfig != null)
            {
                _logger.LogInformation(
                    "Loaded saved config: Interval={Interval}m, OutOfDate={OutOfDate}m, ToDelete={ToDelete}m, LLM={LLM}",
                    savedConfig.IterationIntervalMinutes,
                    savedConfig.OutOfDateThresholdMinutes,
                    savedConfig.ToDeleteThresholdMinutes,
                    savedConfig.LLMProviderName);

                // Apply loaded config to the service
                await _reviewAgentService.UpdateSettingsAsync(savedConfig);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load saved config, using defaults");
        }

        // Publish initial status (Idle, waiting for manual trigger)
        await _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle, nextScheduledAt: null);

        // Wait for manual trigger before first round
        _logger.LogInformation("Review Agent waiting for manual trigger to start first round...");
        await WaitForTriggerAsync(stoppingToken);

        if (stoppingToken.IsCancellationRequested) return;

        // Run first review round (manually triggered)
        await RunReviewRoundSafeAsync(stoppingToken);
        _hasStarted = true;

        // Then run on schedule
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            var interval = TimeSpan.FromMinutes(options.IterationIntervalMinutes);
            var nextScheduledAt = DateTimeOffset.UtcNow + interval;

            // Update next scheduled time
            _reviewAgentService.SetNextScheduledAt(nextScheduledAt);

            // Publish status with next scheduled time
            await _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle, nextScheduledAt);

            _logger.LogInformation(
                "Review Agent sleeping for {Minutes} minutes until next round",
                options.IterationIntervalMinutes);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Run review round
            await RunReviewRoundSafeAsync(stoppingToken);

            // Run cleanup round after review round
            await RunCleanupRoundSafeAsync(stoppingToken);
        }

        _logger.LogInformation("Review Agent hosted service stopped.");
    }

    private async Task WaitForTriggerAsync(CancellationToken stoppingToken)
    {
        _triggerSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using var registration = stoppingToken.Register(() => _triggerSignal.TrySetCanceled());
            await _triggerSignal.Task;
        }
        catch (OperationCanceledException)
        {
            // Service is stopping
        }
    }

    private async Task RunReviewRoundSafeAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested) return;

        _isRunning = true;
        try
        {
            _logger.LogInformation("Starting review round...");

            // Publish status change to working
            await _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.WorkingReviewRound);

            var iteration = await _reviewAgentService.RunReviewRoundAsync(stoppingToken);

            // Persist iteration to storage for history
            try
            {
                await _storage.SaveIterationAsync(iteration, stoppingToken);
                _logger.LogDebug("Saved iteration {IterationId} to storage", iteration.IterationId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save iteration {IterationId} to storage", iteration.IterationId);
            }

            // Publish iteration complete event
            await _eventPublisher.PublishIterationCompleteAsync(
                iteration.IterationId,
                iteration.ToSummary());

            // Publish status change back to idle
            var state = _reviewAgentService.GetState();
            await _eventPublisher.PublishStatusChangeAsync(
                ReviewAgentStatus.Idle,
                state.NextScheduledAt);

            _logger.LogInformation(
                "Review round {IterationId} completed: {Reviewed} reviewed, {Valid} valid, {Deactivated} deactivated",
                iteration.IterationId,
                iteration.NodesReviewed,
                iteration.NodesValid,
                iteration.NodesDeactivated);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Review round was cancelled");
            await _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle);
        }
        catch (Exception ex)
        {
            // T035: Error handling - enter Error state, will retry next interval
            _logger.LogError(ex, "Review round failed with error: {Message}", ex.Message);
            _reviewAgentService.SetError(ex.Message);
            await _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Error);
        }
        finally
        {
            _isRunning = false;
        }
    }

    private async Task RunCleanupRoundSafeAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested) return;

        try
        {
            _logger.LogInformation("Starting scheduled cleanup round...");

            // Publish status change to cleanup
            await _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.WorkingCleanupRound);

            var removedCount = await _reviewAgentService.RunCleanupRoundAsync(stoppingToken);

            // Publish cleanup progress if any nodes were removed
            if (removedCount > 0)
            {
                await _eventPublisher.PublishCleanupProgressAsync(removedCount, []);
            }

            // Publish status change back to idle
            var state = _reviewAgentService.GetState();
            await _eventPublisher.PublishStatusChangeAsync(
                ReviewAgentStatus.Idle,
                state.NextScheduledAt);

            _logger.LogInformation("Cleanup round completed: {Removed} nodes removed", removedCount);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Cleanup round was cancelled");
            await _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle);
        }
        catch (Exception ex)
        {
            // T035: Error handling - log but continue (cleanup failures are non-critical)
            _logger.LogError(ex, "Cleanup round failed with error: {Message}", ex.Message);
            // Don't set error state for cleanup failures, just log
            await _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle);
        }
    }
}
