using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Core.CQRS;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.App.CQRS.Tests;

/// <summary>
/// Unit tests for StateQueryService
/// </summary>
public class StateQueryServiceTests
{
    private readonly Mock<IStateIndexService> _mockIndexService;
    private readonly Mock<ILogger<StateQueryService>> _mockLogger;
    private readonly StateQueryService _service;

    public StateQueryServiceTests()
    {
        _mockIndexService = new Mock<IStateIndexService>();
        _mockLogger = new Mock<ILogger<StateQueryService>>();
        _service = new StateQueryService(_mockIndexService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        _mockIndexService
            .Setup(x => x.GetByIdAsync("TestAgent", "agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StateQueryResult?)null);

        // Act
        var result = await _service.GetByIdAsync("TestAgent", "agent-1");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsState_WhenFound()
    {
        // Arrange
        var expectedResult = new StateQueryResult
        {
            AgentId = "agent-1",
            AgentType = "TestAgent",
            Data = new Dictionary<string, object?> { { "name", "Test" }, { "count", 42 } },
            Version = 5
        };

        _mockIndexService
            .Setup(x => x.GetByIdAsync("TestAgent", "agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.GetByIdAsync("TestAgent", "agent-1");

        // Assert
        result.Should().NotBeNull();
        result!.AgentId.Should().Be("agent-1");
        result.AgentType.Should().Be("TestAgent");
        result.Data.Should().ContainKey("name");
        result.Data["name"].Should().Be("Test");
        result.Version.Should().Be(5);
    }

    [Fact]
    public async Task QueryAsync_ReturnsPagedResults()
    {
        // Arrange
        var queryResult = new PagedStateQueryResult
        {
            TotalCount = 100,
            PageIndex = 0,
            PageSize = 20,
            Items = new List<StateQueryResult>
            {
                new() { AgentId = "agent-1", AgentType = "TestAgent", Version = 1, Data = new() },
                new() { AgentId = "agent-2", AgentType = "TestAgent", Version = 2, Data = new() }
            }
        };

        _mockIndexService
            .Setup(x => x.QueryAsync(It.IsAny<StateQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResult);

        var request = new StateQuery
        {
            AgentType = "TestAgent",
            QueryString = "name:Test*",
            PageIndex = 0,
            PageSize = 20
        };

        // Act
        var result = await _service.QueryAsync(request);

        // Assert
        result.TotalCount.Should().Be(100);
        result.Items.Should().HaveCount(2);
        result.PageIndex.Should().Be(0);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(5);
    }

    [Fact]
    public async Task QueryAsync_PassesQueryParameters()
    {
        // Arrange
        StateQuery? capturedQuery = null;
        _mockIndexService
            .Setup(x => x.QueryAsync(It.IsAny<StateQuery>(), It.IsAny<CancellationToken>()))
            .Callback<StateQuery, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(new PagedStateQueryResult());

        var request = new StateQuery
        {
            AgentType = "TestAgent",
            QueryString = "status:active",
            PageIndex = 2,
            PageSize = 50,
            SortFields = new List<string> { "version:desc" }
        };

        // Act
        await _service.QueryAsync(request);

        // Assert
        capturedQuery.Should().NotBeNull();
        capturedQuery!.AgentType.Should().Be("TestAgent");
        capturedQuery.QueryString.Should().Be("status:active");
        capturedQuery.PageIndex.Should().Be(2);
        capturedQuery.PageSize.Should().Be(50);
        capturedQuery.SortFields.Should().Contain("version:desc");
    }

    [Fact]
    public async Task CountAsync_ReturnsCount()
    {
        // Arrange
        _mockIndexService
            .Setup(x => x.CountAsync("TestAgent", "status:active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        // Act
        var count = await _service.CountAsync("TestAgent", "status:active");

        // Assert
        count.Should().Be(42);
    }

    [Fact]
    public async Task CountAsync_WithNullQuery_CallsIndexService()
    {
        // Arrange
        _mockIndexService
            .Setup(x => x.CountAsync("TestAgent", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var count = await _service.CountAsync("TestAgent", null);

        // Assert
        count.Should().Be(100);
        _mockIndexService.Verify(
            x => x.CountAsync("TestAgent", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsException_WhenIndexServiceFails()
    {
        // Arrange
        _mockIndexService
            .Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _service.GetByIdAsync("TestAgent", "agent-1"));
    }
}

