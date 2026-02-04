using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Aevatar.VibeResearching.Agents.Pivot;
using Shouldly;

namespace VibeResearching.Api.Tests.Vibe.Pivot;

public sealed class DirectionChangeDetectorTests
{
    private readonly ILLMProviderFactory _llmProviderFactory;
    private readonly IAevatarLLMProvider _llmProvider;
    private readonly IOptions<PivotOptions> _options;

    public DirectionChangeDetectorTests()
    {
        _llmProvider = Substitute.For<IAevatarLLMProvider>();
        _llmProviderFactory = Substitute.For<ILLMProviderFactory>();
        _llmProviderFactory.GetDefaultProvider().Returns(_llmProvider);
        _options = Options.Create(new PivotOptions
        {
            ConfidenceThreshold = 0.7,
            ClarificationThreshold = 0.6
        });
    }

    [Fact]
    public async Task DetectAsync_WithHighConfidenceDirectionChange_ReturnsIsDirectionChangeTrue()
    {
        // Arrange
        SetupLlmResponse("""
            {
                "isDirectionChange": true,
                "confidence": 0.9,
                "newTopic": "量子计算在密码学中的应用",
                "preserveAspects": [],
                "needsClarification": false,
                "reasoning": "User explicitly requested to switch research direction"
            }
            """);

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "停止当前研究，我要研究量子计算在密码学中的应用",
            "经典密码学分析");

        // Assert
        result.IsDirectionChange.ShouldBeTrue();
        result.Confidence.ShouldBeGreaterThanOrEqualTo(0.9);
        result.NewTopic.ShouldBe("量子计算在密码学中的应用");
        result.NeedsClarification.ShouldBeFalse();
        result.SessionId.ShouldBe("session1");
        result.MessageId.ShouldBe("msg1");
    }

    [Fact]
    public async Task DetectAsync_WithMediumConfidence_SetsNeedsClarification()
    {
        // Arrange
        SetupLlmResponse("""
            {
                "isDirectionChange": true,
                "confidence": 0.65,
                "newTopic": null,
                "preserveAspects": [],
                "needsClarification": false,
                "reasoning": "User seems dissatisfied but direction unclear"
            }
            """);

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "这个方向不太对",
            "量子计算基础");

        // Assert
        result.IsDirectionChange.ShouldBeTrue();
        result.Confidence.ShouldBeGreaterThanOrEqualTo(0.6);
        result.Confidence.ShouldBeLessThan(0.7);
        result.NeedsClarification.ShouldBeTrue();
    }

    [Fact]
    public async Task DetectAsync_WithLowConfidence_ReturnsIsDirectionChangeFalse()
    {
        // Arrange
        SetupLlmResponse("""
            {
                "isDirectionChange": true,
                "confidence": 0.4,
                "newTopic": null,
                "preserveAspects": [],
                "needsClarification": false,
                "reasoning": "Possible direction change but very uncertain"
            }
            """);

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "可以详细解释一下吗？",
            "密码学基础");

        // Assert
        // When confidence is below ClarificationThreshold, detector should treat as non-change
        result.IsDirectionChange.ShouldBeFalse();
    }

    [Fact]
    public async Task DetectAsync_WithProgressQuery_ReturnsNotDirectionChange()
    {
        // Arrange
        SetupLlmResponse("""
            {
                "isDirectionChange": false,
                "confidence": 0.1,
                "newTopic": null,
                "preserveAspects": [],
                "needsClarification": false,
                "reasoning": "User is asking about research progress, not changing direction"
            }
            """);

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "研究进展如何？",
            "量子计算在密码学中的应用");

        // Assert
        result.IsDirectionChange.ShouldBeFalse();
    }

    [Fact]
    public async Task DetectAsync_WithPreservationHints_ExtractsPreserveAspects()
    {
        // Arrange
        SetupLlmResponse("""
            {
                "isDirectionChange": true,
                "confidence": 0.85,
                "newTopic": "机器学习在图像识别中的应用",
                "preserveAspects": ["CNN架构研究", "损失函数优化方法"],
                "needsClarification": false,
                "reasoning": "User wants to pivot but preserve some existing research"
            }
            """);

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "让我们转向图像识别，但保留之前关于CNN和损失函数的研究",
            "深度学习基础");

        // Assert
        result.IsDirectionChange.ShouldBeTrue();
        result.PreserveAspects.ShouldNotBeEmpty();
        result.PreserveAspects.Count.ShouldBe(2);
        result.PreserveAspects.ShouldContain("CNN架构研究");
        result.PreserveAspects.ShouldContain("损失函数优化方法");
    }

    [Fact]
    public async Task DetectAsync_WithMarkdownCodeBlock_ParsesJsonCorrectly()
    {
        // Arrange - LLM sometimes wraps response in markdown code blocks
        SetupLlmResponse("""
            ```json
            {
                "isDirectionChange": true,
                "confidence": 0.8,
                "newTopic": "自然语言处理",
                "preserveAspects": [],
                "needsClarification": false,
                "reasoning": "Clear direction change"
            }
            ```
            """);

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "我要研究自然语言处理",
            null);

        // Assert
        result.IsDirectionChange.ShouldBeTrue();
        result.NewTopic.ShouldBe("自然语言处理");
    }

    [Fact]
    public async Task DetectAsync_WithInvalidJson_ReturnsDefaultNoChange()
    {
        // Arrange
        SetupLlmResponse("This is not valid JSON");

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "Test message",
            "Current direction");

        // Assert
        result.IsDirectionChange.ShouldBeFalse();
        result.Confidence.ShouldBe(0.0);
    }

    [Fact]
    public async Task DetectAsync_WithLlmException_ReturnsDefaultNoChange()
    {
        // Arrange
        _llmProvider
            .GenerateAsync(Arg.Any<AevatarLLMRequest>(), Arg.Any<CancellationToken>())
            .Returns<AevatarLLMResponse>(_ => throw new InvalidOperationException("LLM error"));

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "Test message",
            "Current direction");

        // Assert
        result.IsDirectionChange.ShouldBeFalse();
        result.Confidence.ShouldBe(0.0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DetectAsync_WithInvalidSessionId_ThrowsArgumentException(string? sessionId)
    {
        var detector = CreateDetector();

        await Should.ThrowAsync<ArgumentException>(async () =>
            await detector.DetectAsync(sessionId!, "msg1", "message", null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DetectAsync_WithInvalidMessageId_ThrowsArgumentException(string? messageId)
    {
        var detector = CreateDetector();

        await Should.ThrowAsync<ArgumentException>(async () =>
            await detector.DetectAsync("session1", messageId!, "message", null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DetectAsync_WithInvalidUserMessage_ThrowsArgumentException(string? userMessage)
    {
        var detector = CreateDetector();

        await Should.ThrowAsync<ArgumentException>(async () =>
            await detector.DetectAsync("session1", "msg1", userMessage!, null));
    }

    [Fact]
    public async Task DetectAsync_ResultIsValid_ReturnsTrue()
    {
        // Arrange
        SetupLlmResponse("""
            {
                "isDirectionChange": true,
                "confidence": 0.85,
                "newTopic": "新方向",
                "preserveAspects": [],
                "needsClarification": false,
                "reasoning": "Clear"
            }
            """);

        var detector = CreateDetector();

        // Act
        var result = await detector.DetectAsync(
            "session1",
            "msg1",
            "Test",
            null);

        // Assert
        result.IsValid().ShouldBeTrue();
    }

    private DirectionChangeDetector CreateDetector()
    {
        return new DirectionChangeDetector(
            _llmProviderFactory,
            _options,
            NullLogger<DirectionChangeDetector>.Instance);
    }

    private void SetupLlmResponse(string content)
    {
        _llmProvider
            .GenerateAsync(Arg.Any<AevatarLLMRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AevatarLLMResponse { Content = content }));
    }
}
