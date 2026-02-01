using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using VibeResearching.Vibe.ReviewAgent;
using Aevatar.VibeResearching.Api.ReviewAgent.Api;
using Aevatar.VibeResearching.Api.ReviewAgent.Storage;
using VibeResearching.Api.Infrastructure;
using System.Reflection;

namespace VibeResearching.Api.Tests.ReviewAgent;

/// <summary>
/// Unit tests for ReviewAgentApi endpoints.
/// Tests T016-T017, T049, T062-T063 from the task list.
/// </summary>
public sealed class ReviewAgentApiTests
{
    private readonly IReviewAgentService _service;
    private readonly IReviewAgentStorage _storage;
    private readonly IReviewAgentTrigger _trigger;

    public ReviewAgentApiTests()
    {
        _service = Substitute.For<IReviewAgentService>();
        _storage = Substitute.For<IReviewAgentStorage>();
        _trigger = Substitute.For<IReviewAgentTrigger>();
        _trigger.IsRunning.Returns(false);
        _trigger.HasStarted.Returns(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  T016: GET /api/review-agent/status
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetStatus_ReturnsOk_WithCorrectShape()
    {
        // Arrange
        var state = new ReviewAgentState
        {
            Status = ReviewAgentStatus.Idle,
            NodesReviewed = 10,
            NodesPending = 5,
            NodesDeactivated = 2,
            NodesRemoved = 1,
            LastCompletedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            NextScheduledAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        _service.GetState().Returns(state);

        // Act
        var result = InvokeGetStatus(_service, _trigger);

        // Assert
        result.ShouldBeOfType<Ok<ReviewAgentStatusResponse>>();
        var okResult = (Ok<ReviewAgentStatusResponse>)result;
        okResult.Value.ShouldNotBeNull();
        okResult.Value.Status.ShouldBe("Idle");
        okResult.Value.NodesReviewed.ShouldBe(10);
        okResult.Value.NodesPending.ShouldBe(5);
        okResult.Value.NodesDeactivated.ShouldBe(2);
        okResult.Value.NodesRemoved.ShouldBe(1);
    }

    [Fact]
    public void GetStatus_IncludesErrorMessage_WhenPresent()
    {
        // Arrange
        var state = new ReviewAgentState
        {
            Status = ReviewAgentStatus.Error,
            ErrorMessage = "LLM provider unavailable"
        };
        _service.GetState().Returns(state);

        // Act
        var result = InvokeGetStatus(_service, _trigger);

        // Assert
        var okResult = (Ok<ReviewAgentStatusResponse>)result;
        okResult.Value!.Status.ShouldBe("Error");
        okResult.Value.ErrorMessage.ShouldBe("LLM provider unavailable");
    }

    // ─────────────────────────────────────────────────────────────
    //  T062: GET /api/review-agent/settings
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetSettings_ReturnsOk_WithCurrentSettings()
    {
        // Arrange
        var options = new ReviewAgentOptions
        {
            IterationIntervalMinutes = 60,
            OutOfDateThresholdMinutes = 1440,
            ToDeleteThresholdMinutes = 10080,
            LLMProviderName = "deepseek",
            PerNodeTimeoutSeconds = 120
        };
        _service.GetSettings().Returns(options);

        // Act
        var result = InvokeGetSettings(_service);

        // Assert
        result.ShouldBeOfType<Ok<ReviewAgentOptionsResponse>>();
        var okResult = (Ok<ReviewAgentOptionsResponse>)result;
        okResult.Value!.IterationIntervalMinutes.ShouldBe(60);
        okResult.Value.OutOfDateThresholdMinutes.ShouldBe(1440);
        okResult.Value.ToDeleteThresholdMinutes.ShouldBe(10080);
        okResult.Value.LLMProviderName.ShouldBe("deepseek");
        okResult.Value.PerNodeTimeoutSeconds.ShouldBe(120);
    }

    // ─────────────────────────────────────────────────────────────
    //  T063: PUT /api/review-agent/settings Validation
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSettings_ReturnsBadRequest_WhenIterationIntervalTooSmall()
    {
        // Arrange
        var update = new ReviewAgentSettingsUpdate(IterationIntervalMinutes: 0);
        _service.GetSettings().Returns(new ReviewAgentOptions());

        // Act
        var result = await InvokeUpdateSettings(update, _service, _storage);

        // Assert - Check the type name contains "BadRequest" since the generic type is anonymous
        result.GetType().Name.ShouldContain("BadRequest");
    }

    [Fact]
    public async Task UpdateSettings_ReturnsBadRequest_WhenOutOfDateThresholdTooSmall()
    {
        // Arrange
        var update = new ReviewAgentSettingsUpdate(OutOfDateThresholdMinutes: 0);
        _service.GetSettings().Returns(new ReviewAgentOptions());

        // Act
        var result = await InvokeUpdateSettings(update, _service, _storage);

        // Assert
        result.GetType().Name.ShouldContain("BadRequest");
    }

    [Fact]
    public async Task UpdateSettings_ReturnsBadRequest_WhenToDeleteThresholdTooSmall()
    {
        // Arrange
        var update = new ReviewAgentSettingsUpdate(ToDeleteThresholdMinutes: 30);
        _service.GetSettings().Returns(new ReviewAgentOptions());

        // Act
        var result = await InvokeUpdateSettings(update, _service, _storage);

        // Assert
        result.GetType().Name.ShouldContain("BadRequest");
    }

    [Fact]
    public async Task UpdateSettings_ReturnsBadRequest_WhenPerNodeTimeoutTooSmall()
    {
        // Arrange
        var update = new ReviewAgentSettingsUpdate(PerNodeTimeoutSeconds: 5);
        _service.GetSettings().Returns(new ReviewAgentOptions());

        // Act
        var result = await InvokeUpdateSettings(update, _service, _storage);

        // Assert
        result.GetType().Name.ShouldContain("BadRequest");
    }

    [Fact]
    public async Task UpdateSettings_ReturnsOk_WithValidUpdate()
    {
        // Arrange
        var currentOptions = new ReviewAgentOptions
        {
            IterationIntervalMinutes = 60,
            OutOfDateThresholdMinutes = 1440,
            ToDeleteThresholdMinutes = 10080,
            LLMProviderName = "deepseek",
            PerNodeTimeoutSeconds = 120
        };
        var update = new ReviewAgentSettingsUpdate(
            IterationIntervalMinutes: 30,
            OutOfDateThresholdMinutes: 720
        );

        _service.GetSettings().Returns(currentOptions);
        _service.UpdateSettingsAsync(Arg.Any<ReviewAgentOptions>())
            .Returns(callInfo =>
            {
                var opts = callInfo.Arg<ReviewAgentOptions>();
                return Task.FromResult(opts);
            });

        // Act
        var result = await InvokeUpdateSettings(update, _service, _storage);

        // Assert
        result.ShouldBeOfType<Ok<ReviewAgentOptionsResponse>>();
        var okResult = (Ok<ReviewAgentOptionsResponse>)result;
        okResult.Value!.IterationIntervalMinutes.ShouldBe(30);
        okResult.Value.OutOfDateThresholdMinutes.ShouldBe(720);
        // Unchanged values should be preserved
        okResult.Value.ToDeleteThresholdMinutes.ShouldBe(10080);
    }

    [Fact]
    public async Task UpdateSettings_PersistsToStorage()
    {
        // Arrange
        var update = new ReviewAgentSettingsUpdate(IterationIntervalMinutes: 30);
        _service.GetSettings().Returns(new ReviewAgentOptions
        {
            IterationIntervalMinutes = 60,
            OutOfDateThresholdMinutes = 1440,
            ToDeleteThresholdMinutes = 10080,
            LLMProviderName = "deepseek",
            PerNodeTimeoutSeconds = 120
        });
        _service.UpdateSettingsAsync(Arg.Any<ReviewAgentOptions>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<ReviewAgentOptions>()));

        // Act
        await InvokeUpdateSettings(update, _service, _storage);

        // Assert
        await _storage.Received(1).SaveConfigAsync(
            Arg.Is<ReviewAgentOptions>(o => o.IterationIntervalMinutes == 30),
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────
    //  T049: GET /api/review-agent/iterations
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetIterations_ReturnsOk_WithPaginatedList()
    {
        // Arrange
        var iterations = new List<ReviewIterationSummary>
        {
            new()
            {
                IterationId = "iter-1",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
                CompletedAt = DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(5),
                NodesReviewed = 10,
                NodesValid = 8,
                NodesDeactivated = 2
            },
            new()
            {
                IterationId = "iter-2",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                CompletedAt = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(3),
                NodesReviewed = 5,
                NodesValid = 5,
                NodesDeactivated = 0
            }
        };

        _storage.LoadIterationsAsync(10, 0, Arg.Any<CancellationToken>())
            .Returns(new IterationListResponse
            {
                Iterations = iterations,
                Total = 2,
                Limit = 10,
                Offset = 0
            });

        // Act
        var result = await InvokeGetIterations(_storage, 10, 0);

        // Assert
        result.ShouldBeOfType<Ok<IterationListResponse>>();
        var okResult = (Ok<IterationListResponse>)result;
        okResult.Value!.Iterations.Count.ShouldBe(2);
        okResult.Value.Total.ShouldBe(2);
    }

    [Fact]
    public async Task GetIteration_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        _storage.LoadIterationAsync("nonexistent", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReviewIteration?>(null));

        // Act
        var result = await InvokeGetIteration("nonexistent", _storage);

        // Assert
        result.GetType().Name.ShouldContain("NotFound");
    }

    [Fact]
    public async Task GetIteration_ReturnsOk_WhenExists()
    {
        // Arrange
        var iteration = new ReviewIteration
        {
            IterationId = "iter-123",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(5),
            NodesReviewed = 10,
            NodesValid = 8,
            NodesDeactivated = 2
        };

        _storage.LoadIterationAsync("iter-123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReviewIteration?>(iteration));

        // Act
        var result = await InvokeGetIteration("iter-123", _storage);

        // Assert
        result.ShouldBeOfType<Ok<ReviewIteration>>();
        var okResult = (Ok<ReviewIteration>)result;
        okResult.Value!.IterationId.ShouldBe("iter-123");
    }

    // ─────────────────────────────────────────────────────────────
    //  T017: Performance Requirement
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetStatus_CompletesQuickly()
    {
        // Arrange
        _service.GetState().Returns(new ReviewAgentState());

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            InvokeGetStatus(_service, _trigger);
        }
        sw.Stop();

        // Assert - 100 calls should complete in well under 2 seconds
        sw.ElapsedMilliseconds.ShouldBeLessThan(2000);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers - Use reflection to call private static methods
    // ─────────────────────────────────────────────────────────────

    private static IResult InvokeGetStatus(IReviewAgentService service, IReviewAgentTrigger trigger)
    {
        var method = typeof(ReviewAgentApi).GetMethod("GetStatus",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (IResult)method!.Invoke(null, [service, trigger])!;
    }

    private static IResult InvokeGetSettings(IReviewAgentService service)
    {
        var method = typeof(ReviewAgentApi).GetMethod("GetSettings",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (IResult)method!.Invoke(null, [service])!;
    }

    private static async Task<IResult> InvokeUpdateSettings(
        ReviewAgentSettingsUpdate update,
        IReviewAgentService service,
        IReviewAgentStorage storage)
    {
        var method = typeof(ReviewAgentApi).GetMethod("UpdateSettings",
            BindingFlags.NonPublic | BindingFlags.Static);
        var task = (Task<IResult>)method!.Invoke(null, [update, service, storage, CancellationToken.None])!;
        return await task;
    }

    private static async Task<IResult> InvokeGetIterations(
        IReviewAgentStorage storage,
        int limit = 10,
        int offset = 0)
    {
        var method = typeof(ReviewAgentApi).GetMethod("GetIterations",
            BindingFlags.NonPublic | BindingFlags.Static);
        var task = (Task<IResult>)method!.Invoke(null, [storage, limit, offset, CancellationToken.None])!;
        return await task;
    }

    private static async Task<IResult> InvokeGetIteration(
        string iterationId,
        IReviewAgentStorage storage)
    {
        var method = typeof(ReviewAgentApi).GetMethod("GetIteration",
            BindingFlags.NonPublic | BindingFlags.Static);
        var task = (Task<IResult>)method!.Invoke(null, [iterationId, storage, CancellationToken.None])!;
        return await task;
    }
}
