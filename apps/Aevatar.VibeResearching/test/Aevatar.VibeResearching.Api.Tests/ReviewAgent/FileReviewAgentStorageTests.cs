using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using VibeResearching.Vibe.ReviewAgent;
using Aevatar.VibeResearching.Api.ReviewAgent.Storage;

namespace VibeResearching.Api.Tests.ReviewAgent;

/// <summary>
/// Unit tests for FileReviewAgentStorage.
/// Tests file-based persistence for iterations and config.
/// </summary>
public sealed class FileReviewAgentStorageTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly FileReviewAgentStorage _storage;

    public FileReviewAgentStorageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"review-agent-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        _storage = new FileReviewAgentStorage(
            _testDirectory,
            NullLogger<FileReviewAgentStorage>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  State Persistence
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveStateAsync_CreatesFile()
    {
        // Arrange
        var state = new ReviewAgentState
        {
            Status = ReviewAgentStatus.Idle,
            NodesReviewed = 10,
            NodesDeactivated = 2
        };

        // Act
        await _storage.SaveStateAsync(state);

        // Assert
        var stateFile = Path.Combine(_testDirectory, "state.json");
        File.Exists(stateFile).ShouldBeTrue();
    }

    [Fact]
    public async Task LoadStateAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        // Act
        var state = await _storage.LoadStateAsync();

        // Assert
        state.ShouldBeNull();
    }

    [Fact]
    public async Task LoadStateAsync_ReturnsState_WhenFileExists()
    {
        // Arrange
        var original = new ReviewAgentState
        {
            Status = ReviewAgentStatus.Error,
            ErrorMessage = "Test error",
            NodesReviewed = 50,
            NodesDeactivated = 5,
            NodesRemoved = 3
        };
        await _storage.SaveStateAsync(original);

        // Act
        var loaded = await _storage.LoadStateAsync();

        // Assert
        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(ReviewAgentStatus.Error);
        loaded.ErrorMessage.ShouldBe("Test error");
        loaded.NodesReviewed.ShouldBe(50);
        loaded.NodesDeactivated.ShouldBe(5);
        loaded.NodesRemoved.ShouldBe(3);
    }

    // ─────────────────────────────────────────────────────────────
    //  Config Persistence
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveConfigAsync_CreatesFile()
    {
        // Arrange
        var config = new ReviewAgentOptions
        {
            IterationIntervalMinutes = 30,
            OutOfDateThresholdMinutes = 720,
            ToDeleteThresholdMinutes = 5040,
            LLMProviderName = "test-provider",
            PerNodeTimeoutSeconds = 90
        };

        // Act
        await _storage.SaveConfigAsync(config);

        // Assert
        var configFile = Path.Combine(_testDirectory, "config.json");
        File.Exists(configFile).ShouldBeTrue();
    }

    [Fact]
    public async Task LoadConfigAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        // Act
        var config = await _storage.LoadConfigAsync();

        // Assert
        config.ShouldBeNull();
    }

    [Fact]
    public async Task LoadConfigAsync_ReturnsConfig_WhenFileExists()
    {
        // Arrange
        var original = new ReviewAgentOptions
        {
            IterationIntervalMinutes = 45,
            OutOfDateThresholdMinutes = 1000,
            ToDeleteThresholdMinutes = 8000,
            LLMProviderName = "custom-llm",
            PerNodeTimeoutSeconds = 180
        };
        await _storage.SaveConfigAsync(original);

        // Act
        var loaded = await _storage.LoadConfigAsync();

        // Assert
        loaded.ShouldNotBeNull();
        loaded.IterationIntervalMinutes.ShouldBe(45);
        loaded.OutOfDateThresholdMinutes.ShouldBe(1000);
        loaded.ToDeleteThresholdMinutes.ShouldBe(8000);
        loaded.LLMProviderName.ShouldBe("custom-llm");
        loaded.PerNodeTimeoutSeconds.ShouldBe(180);
    }

    // ─────────────────────────────────────────────────────────────
    //  Iteration Persistence
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveIterationAsync_CreatesFile()
    {
        // Arrange
        var iteration = CreateTestIteration("test-iter-001");

        // Act
        await _storage.SaveIterationAsync(iteration);

        // Assert
        var iterationsDir = Path.Combine(_testDirectory, "iterations");
        Directory.Exists(iterationsDir).ShouldBeTrue();
        Directory.GetFiles(iterationsDir, "*.json").Length.ShouldBe(1);
    }

    [Fact]
    public async Task LoadIterationAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var iteration = await _storage.LoadIterationAsync("nonexistent");

        // Assert
        iteration.ShouldBeNull();
    }

    [Fact]
    public async Task LoadIterationAsync_ReturnsIteration_WhenExists()
    {
        // Arrange
        var original = CreateTestIteration("iter-123");
        original.Entries.Add(new ReviewLogEntry
        {
            EntryId = "entry-1",
            NodeId = "node-1",
            NodeLabel = "Test Node",
            ReviewResult = ReviewResult.Passed,
            Timestamp = DateTimeOffset.UtcNow
        });
        await _storage.SaveIterationAsync(original);

        // Act
        var loaded = await _storage.LoadIterationAsync("iter-123");

        // Assert
        loaded.ShouldNotBeNull();
        loaded.IterationId.ShouldBe("iter-123");
        loaded.Entries.Count.ShouldBe(1);
        loaded.Entries[0].NodeLabel.ShouldBe("Test Node");
    }

    [Fact]
    public async Task LoadIterationsAsync_ReturnsPaginatedResults()
    {
        // Arrange
        for (var i = 0; i < 15; i++)
        {
            var iteration = CreateTestIteration($"iter-{i:D3}");
            await _storage.SaveIterationAsync(iteration);
        }

        // Act
        var result = await _storage.LoadIterationsAsync(limit: 5, offset: 0);

        // Assert
        result.Iterations.Count.ShouldBe(5);
        result.Total.ShouldBe(15);
        result.Limit.ShouldBe(5);
        result.Offset.ShouldBe(0);
    }

    [Fact]
    public async Task LoadIterationsAsync_RespectsOffset()
    {
        // Arrange
        for (var i = 0; i < 10; i++)
        {
            var iteration = CreateTestIteration($"iter-{i:D3}");
            await _storage.SaveIterationAsync(iteration);
        }

        // Act
        var result = await _storage.LoadIterationsAsync(limit: 5, offset: 5);

        // Assert
        result.Iterations.Count.ShouldBe(5);
        result.Offset.ShouldBe(5);
    }

    [Fact]
    public async Task LoadIterationsAsync_ReturnsEmpty_WhenNoIterations()
    {
        // Act
        var result = await _storage.LoadIterationsAsync(limit: 10, offset: 0);

        // Assert
        result.Iterations.ShouldBeEmpty();
        result.Total.ShouldBe(0);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static ReviewIteration CreateTestIteration(string id)
    {
        return new ReviewIteration
        {
            IterationId = id,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            NodesReviewed = 10,
            NodesValid = 8,
            NodesDeactivated = 2,
            NodesRemoved = 0
        };
    }
}
