using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.App.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.App.CQRS.Tests;

/// <summary>
/// Unit tests for StateQueryController
/// </summary>
public class StateQueryControllerTests
{
    private readonly Mock<IStateQueryService> _mockService;
    private readonly Mock<ILogger<StateQueryController>> _mockLogger;
    private readonly StateQueryController _controller;

    public StateQueryControllerTests()
    {
        _mockService = new Mock<IStateQueryService>();
        _mockLogger = new Mock<ILogger<StateQueryController>>();
        _controller = new StateQueryController(_mockService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenStateNotExists()
    {
        // Arrange
        _mockService
            .Setup(x => x.GetByIdAsync("TestAgent", "agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StateQueryResult?)null);

        // Act
        var result = await _controller.GetById("TestAgent", "agent-1");

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_ReturnsOk_WhenStateExists()
    {
        // Arrange
        var state = new StateQueryResult
        {
            AgentId = "agent-1",
            AgentType = "TestAgent",
            Data = new Dictionary<string, object?> { { "name", "Test" } },
            Version = 1
        };

        _mockService
            .Setup(x => x.GetByIdAsync("TestAgent", "agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        // Act
        var result = await _controller.GetById("TestAgent", "agent-1");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        okResult.Value.Should().BeEquivalentTo(state);
    }

    [Fact]
    public async Task GetById_Returns500_OnException()
    {
        // Arrange
        _mockService
            .Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetById("TestAgent", "agent-1");

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)result.Result!;
        objectResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Query_ReturnsPagedResults()
    {
        // Arrange
        var pagedResult = new PagedStateQueryResult
        {
            TotalCount = 50,
            Items = new List<StateQueryResult>
            {
                new() { AgentId = "agent-1", AgentType = "TestAgent", Version = 1 },
                new() { AgentId = "agent-2", AgentType = "TestAgent", Version = 2 }
            },
            PageIndex = 0,
            PageSize = 20,
        };

        _mockService
            .Setup(x => x.QueryAsync(It.IsAny<StateQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        var request = new StateQuery
        {
            AgentType = "TestAgent",
            PageIndex = 0,
            PageSize = 20
        };

        // Act
        var result = await _controller.Query(request);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PagedStateQueryResult;
        response.Should().NotBeNull();
        response!.TotalCount.Should().Be(50);
        response.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Query_PassesQueryStringToService()
    {
        // Arrange
        StateQuery? capturedRequest = null;
        _mockService
            .Setup(x => x.QueryAsync(It.IsAny<StateQuery>(), It.IsAny<CancellationToken>()))
            .Callback<StateQuery, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new PagedStateQueryResult());

        var request = new StateQuery
        {
            AgentType = "TestAgent",
            QueryString = "status:active AND version:>5",
            PageIndex = 1,
            PageSize = 10
        };

        // Act
        await _controller.Query(request);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.QueryString.Should().Be("status:active AND version:>5");
        capturedRequest.PageIndex.Should().Be(1);
        capturedRequest.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Query_Returns500_OnException()
    {
        // Arrange
        _mockService
            .Setup(x => x.QueryAsync(It.IsAny<StateQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Query failed"));

        var request = new StateQuery { AgentType = "TestAgent" };

        // Act
        var result = await _controller.Query(request);

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)result.Result!;
        objectResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Count_ReturnsCount()
    {
        // Arrange
        _mockService
            .Setup(x => x.CountAsync("TestAgent", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        // Act
        var result = await _controller.Count("TestAgent", null);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as StateCountResponseDto;
        response.Should().NotBeNull();
        response!.AgentType.Should().Be("TestAgent");
        response.Count.Should().Be(42);
    }

    [Fact]
    public async Task Count_WithQueryString_PassesToService()
    {
        // Arrange
        _mockService
            .Setup(x => x.CountAsync("TestAgent", "active:true", It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        // Act
        var result = await _controller.Count("TestAgent", "active:true");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        _mockService.Verify(x => x.CountAsync("TestAgent", "active:true", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Count_Returns500_OnException()
    {
        // Arrange
        _mockService
            .Setup(x => x.CountAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Count failed"));

        // Act
        var result = await _controller.Count("TestAgent", null);

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)result.Result!;
        objectResult.StatusCode.Should().Be(500);
    }
}

/// <summary>
/// Tests for StateQueryController DTOs
/// </summary>
public class StateQueryControllerDtoTests
{
    [Fact]
    public void StateQuery_HasDefaultValues()
    {
        // Act
        var dto = new StateQuery();

        // Assert
        dto.AgentType.Should().BeEmpty();
        dto.QueryString.Should().BeNull();
        dto.PageIndex.Should().Be(0);
        dto.PageSize.Should().Be(20);
        dto.SortFields.Should().NotBeNull();
        dto.SortFields.Should().BeEmpty();
    }

    [Fact]
    public void StateQueryResult_HasDefaultValues()
    {
        // Act
        var dto = new StateQueryResult();

        // Assert
        dto.AgentId.Should().BeEmpty();
        dto.AgentType.Should().BeEmpty();
        dto.Data.Should().NotBeNull();
        dto.Version.Should().Be(0);
    }

    [Fact]
    public void PagedStateQueryResult_HasDefaultValues()
    {
        // Act
        var dto = new PagedStateQueryResult();

        // Assert
        dto.TotalCount.Should().Be(0);
        dto.Items.Should().NotBeNull();
        dto.Items.Should().BeEmpty();
        dto.PageIndex.Should().Be(0);
        dto.PageSize.Should().Be(0);
        dto.TotalPages.Should().Be(0);
    }

    [Fact]
    public void StateCountResponseDto_HasDefaultValues()
    {
        // Act
        var dto = new StateCountResponseDto();

        // Assert
        dto.AgentType.Should().BeEmpty();
        dto.Count.Should().Be(0);
    }
}

