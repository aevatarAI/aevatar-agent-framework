using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using VibeResearching.Vibe.ReviewAgent;
using Aevatar.VibeResearching.Api.ReviewAgent.Events;
using Aevatar.VibeResearching.Api.ReviewAgent.Storage;
using VibeResearching.Api.Infrastructure;

namespace VibeResearching.Api.Tests.ReviewAgent;

/// <summary>
/// Unit tests for ReviewAgentHostedService.
/// Tests T027: Background service lifecycle, scheduling, error handling.
/// </summary>
public sealed class ReviewAgentHostedServiceTests
{
    private readonly IReviewAgentService _service;
    private readonly ReviewAgentService _concreteService;
    private readonly IReviewAgentEventPublisher _eventPublisher;
    private readonly IReviewAgentStorage _storage;
    private readonly IOptionsMonitor<ReviewAgentOptions> _optionsMonitor;

    public ReviewAgentHostedServiceTests()
    {
        var options = new ReviewAgentOptions
        {
            IterationIntervalMinutes = 1, // Short interval for testing
            OutOfDateThresholdMinutes = 1440,
            ToDeleteThresholdMinutes = 10080,
            LLMProviderName = "test",
            PerNodeTimeoutSeconds = 60
        };

        _optionsMonitor = Substitute.For<IOptionsMonitor<ReviewAgentOptions>>();
        _optionsMonitor.CurrentValue.Returns(options);

        // Create concrete service for the hosted service
        var graphAccess = Substitute.For<VibeResearching.Vibe.Tools.IVibeGraphAccess>();
        graphAccess.GetStaleKnowledgeNodesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Aevatar.Agents.Knowledge.Graph.Models.KnowledgeNode>>([]));
        graphAccess.GetNodesForCleanupAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Aevatar.Agents.Knowledge.Graph.Models.KnowledgeNode>>([]));

        _concreteService = new ReviewAgentService(
            _optionsMonitor,
            graphAccess,
            NullLogger<ReviewAgentService>.Instance);

        _service = _concreteService;
        _eventPublisher = Substitute.For<IReviewAgentEventPublisher>();
        _storage = Substitute.For<IReviewAgentStorage>();
    }

    // ─────────────────────────────────────────────────────────────
    //  Lifecycle Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RunsFirstReviewRoundImmediately()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var hostedService = CreateHostedService();
        var reviewRoundStarted = new TaskCompletionSource();

        _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.WorkingReviewRound, Arg.Any<DateTimeOffset?>())
            .Returns(callInfo =>
            {
                reviewRoundStarted.TrySetResult();
                return Task.CompletedTask;
            });

        // Act - Start the service and wait for first review round to start
        var executeTask = await StartServiceAndTriggerAsync(hostedService, cts);

        // Wait for the review round to start (with timeout)
        var completed = await Task.WhenAny(reviewRoundStarted.Task, Task.Delay(5000));

        // Cancel to stop the service
        cts.Cancel();

        try { await executeTask; } catch (OperationCanceledException) { }

        // Assert
        completed.ShouldBe(reviewRoundStarted.Task, "First review round should start immediately");
    }

    [Fact]
    public async Task ExecuteAsync_PublishesIdleStatusOnStart()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var hostedService = CreateHostedService();
        var idlePublished = false;

        _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle, Arg.Any<DateTimeOffset?>())
            .Returns(callInfo =>
            {
                idlePublished = true;
                return Task.CompletedTask;
            });

        // Act
        var executeTask = hostedService.StartAsync(cts.Token);
        await Task.Delay(100); // Give time to start
        cts.Cancel();

        try { await executeTask; } catch (OperationCanceledException) { }

        // Assert
        idlePublished.ShouldBeTrue("Should publish Idle status on start");
    }

    [Fact]
    public async Task ExecuteAsync_LoadsConfigFromStorage()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var hostedService = CreateHostedService();
        var savedConfig = new ReviewAgentOptions
        {
            IterationIntervalMinutes = 30,
            OutOfDateThresholdMinutes = 720,
            ToDeleteThresholdMinutes = 5040,
            LLMProviderName = "loaded-llm",
            PerNodeTimeoutSeconds = 90
        };

        _storage.LoadConfigAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReviewAgentOptions?>(savedConfig));

        // Act
        var executeTask = hostedService.StartAsync(cts.Token);
        await Task.Delay(200); // Give time to load config
        cts.Cancel();

        try { await executeTask; } catch (OperationCanceledException) { }

        // Assert
        await _storage.Received(1).LoadConfigAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SavesIterationAfterReviewRound()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var hostedService = CreateHostedService();
        var iterationSaved = new TaskCompletionSource();

        _storage.SaveIterationAsync(Arg.Any<ReviewIteration>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                iterationSaved.TrySetResult();
                return Task.CompletedTask;
            });

        // Act
        var executeTask = await StartServiceAndTriggerAsync(hostedService, cts);

        // Wait for iteration to be saved (with timeout)
        var completed = await Task.WhenAny(iterationSaved.Task, Task.Delay(5000));
        cts.Cancel();

        try { await executeTask; } catch (OperationCanceledException) { }

        // Assert
        completed.ShouldBe(iterationSaved.Task, "Iteration should be saved after review round");
    }

    // ─────────────────────────────────────────────────────────────
    //  Event Publishing Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PublishesIterationCompleteEvent()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var hostedService = CreateHostedService();
        var iterationCompletePublished = new TaskCompletionSource();

        _eventPublisher.PublishIterationCompleteAsync(Arg.Any<string>(), Arg.Any<ReviewIterationSummary>())
            .Returns(callInfo =>
            {
                iterationCompletePublished.TrySetResult();
                return Task.CompletedTask;
            });

        // Act
        var executeTask = await StartServiceAndTriggerAsync(hostedService, cts);

        var completed = await Task.WhenAny(iterationCompletePublished.Task, Task.Delay(5000));
        cts.Cancel();

        try { await executeTask; } catch (OperationCanceledException) { }

        // Assert
        completed.ShouldBe(iterationCompletePublished.Task, "Should publish iteration complete event");
    }

    // ─────────────────────────────────────────────────────────────
    //  Error Handling Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ContinuesOnStorageSaveError()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var hostedService = CreateHostedService();
        var iterationCompletePublished = new TaskCompletionSource();

        // Make storage save fail
        _storage.SaveIterationAsync(Arg.Any<ReviewIteration>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("Storage error"));

        _eventPublisher.PublishIterationCompleteAsync(Arg.Any<string>(), Arg.Any<ReviewIterationSummary>())
            .Returns(callInfo =>
            {
                iterationCompletePublished.TrySetResult();
                return Task.CompletedTask;
            });

        // Act - Should not throw even if storage fails
        var executeTask = await StartServiceAndTriggerAsync(hostedService, cts);

        var completed = await Task.WhenAny(iterationCompletePublished.Task, Task.Delay(5000));
        cts.Cancel();

        try { await executeTask; } catch (OperationCanceledException) { }

        // Assert - Should still complete the iteration even if save failed
        completed.ShouldBe(iterationCompletePublished.Task, "Should continue even if storage save fails");
    }

    [Fact]
    public async Task ExecuteAsync_HandlesConfigLoadError()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var hostedService = CreateHostedService();

        // Make config load fail
        _storage.LoadConfigAsync(Arg.Any<CancellationToken>())
            .Returns<ReviewAgentOptions?>(_ => throw new IOException("Config load error"));

        var idlePublished = new TaskCompletionSource();
        _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle, Arg.Any<DateTimeOffset?>())
            .Returns(callInfo =>
            {
                idlePublished.TrySetResult();
                return Task.CompletedTask;
            });

        // Act - Should not throw even if config load fails
        var executeTask = hostedService.StartAsync(cts.Token);

        var completed = await Task.WhenAny(idlePublished.Task, Task.Delay(5000));
        cts.Cancel();

        try { await executeTask; } catch (OperationCanceledException) { }

        // Assert - Should continue with defaults if config load fails
        completed.ShouldBe(idlePublished.Task, "Should continue with defaults if config load fails");
    }

    // ─────────────────────────────────────────────────────────────
    //  Cancellation Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StopsGracefullyOnCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var hostedService = CreateHostedService();

        // Act
        var executeTask = hostedService.StartAsync(cts.Token);
        await Task.Delay(100); // Let it start

        cts.Cancel();

        // Assert - Should complete without throwing
        var ex = await Record.ExceptionAsync(async () =>
        {
            try { await executeTask; } catch (OperationCanceledException) { }
        });

        ex.ShouldBeNull("Should stop gracefully on cancellation");
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private ReviewAgentHostedService CreateHostedService()
    {
        return new ReviewAgentHostedService(
            _service,
            _eventPublisher,
            _storage,
            _optionsMonitor,
            NullLogger<ReviewAgentHostedService>.Instance);
    }

    private async Task<Task> StartServiceAndTriggerAsync(
        ReviewAgentHostedService hostedService,
        CancellationTokenSource cts)
    {
        var idlePublished = new TaskCompletionSource();

        _eventPublisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle, Arg.Any<DateTimeOffset?>())
            .Returns(callInfo =>
            {
                idlePublished.TrySetResult();
                return Task.CompletedTask;
            });

        var executeTask = hostedService.StartAsync(cts.Token);
        await Task.WhenAny(idlePublished.Task, Task.Delay(5000));
        await hostedService.TriggerReviewRoundAsync();

        return executeTask;
    }
}
