using System.ComponentModel;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Tests.ToolManager;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Abstractions.Tests;

/// <summary>
/// Tests for Tool System interfaces and implementations
/// </summary>
public class ToolSystemTests
{
    private readonly Mock<ILogger> _mockLogger;

    public ToolSystemTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    #region IAevatarTool Tests

    [Fact]
    [DisplayName("Tool ExecuteAsync with valid parameters should return result")]
    public async Task Tool_ExecuteAsync_WithValidParameters_ShouldReturnResult()
    {
        // Arrange
        var tool = new TestCalculatorTool();
        var parameters = new Dictionary<string, object>
        {
            ["operation"] = "add",
            ["a"] = 5,
            ["b"] = 3
        };
        var context = new ToolContext { AgentId = "test-agent" };

        // Act
        var result = await tool.ExecuteAsync(parameters, context, _mockLogger.Object);

        // Assert
        result.ShouldNotBeNull();
        // result.ShouldBe(8);
    }

    [Fact]
    [DisplayName("Tool ValidateParameters with invalid input should detect errors")]
    public void Tool_ValidateParameters_WithInvalidInput_ShouldDetectErrors()
    {
        // Arrange
        var tool = new TestCalculatorTool();
        var invalidParams = new Dictionary<string, object?>
        {
            ["operation"] = "invalid_op",
            ["a"] = 5,
            ["b"] = 3
        };

        // Act
        var validation = tool.ValidateParameters(invalidParams);

        // Assert
        validation.IsValid.ShouldBeFalse();
        validation.Errors.ShouldNotBeEmpty();
        validation.Errors[0].ShouldContain("not in allowed values");
    }

    [Fact]
    [DisplayName("Tool ValidateParameters with missing required parameters should fail")]
    public void Tool_ValidateParameters_WithMissingRequired_ShouldFail()
    {
        // Arrange
        var tool = new TestCalculatorTool();
        var invalidParams = new Dictionary<string, object?>
        {
            ["operation"] = "add"
            // Missing 'a' and 'b'
        };

        // Act
        var validation = tool.ValidateParameters(invalidParams);

        // Assert
        validation.IsValid.ShouldBeFalse();
        validation.Errors.ShouldContain(e => e.Contains("'a' is missing"));
        validation.Errors.ShouldContain(e => e.Contains("'b' is missing"));
    }

    [Fact]
    [DisplayName("Tool ValidateParameters should accept whole-number doubles for integer parameters")]
    public void Tool_ValidateParameters_Integer_WithWholeNumberDouble_ShouldPass()
    {
        // Arrange
        var tool = new SlowTool();
        var parameters = new Dictionary<string, object?>
        {
            ["delay"] = 5000.0
        };

        // Act
        var validation = tool.ValidateParameters(parameters);

        // Assert
        validation.IsValid.ShouldBeTrue();
        validation.Errors.ShouldBeEmpty();
    }

    [Fact]
    [DisplayName("Tool ValidateParameters should accept JsonElement number for integer parameters")]
    public void Tool_ValidateParameters_Integer_WithJsonElementNumber_ShouldPass()
    {
        // Arrange
        var tool = new SlowTool();
        using var doc = JsonDocument.Parse("5000");
        var parameters = new Dictionary<string, object?>
        {
            ["delay"] = doc.RootElement
        };

        // Act
        var validation = tool.ValidateParameters(parameters);

        // Assert
        validation.IsValid.ShouldBeTrue();
        validation.Errors.ShouldBeEmpty();
    }

    [Fact]
    [DisplayName("Tool ValidateParameters should reject non-integer numbers for integer parameters")]
    public void Tool_ValidateParameters_Integer_WithNonWholeNumber_ShouldFail()
    {
        // Arrange
        var tool = new SlowTool();
        var parameters = new Dictionary<string, object?>
        {
            ["delay"] = 1.5
        };

        // Act
        var validation = tool.ValidateParameters(parameters);

        // Assert
        validation.IsValid.ShouldBeFalse();
        validation.Errors.ShouldContain(e => e.Contains("type mismatch"));
    }

    [Fact]
    [DisplayName("Tool CreateToolDefinition should include all metadata")]
    public void Tool_CreateToolDefinition_ShouldIncludeMetadata()
    {
        // Arrange
        var tool = new TestWeatherTool();
        var context = new ToolContext { AgentId = "weather-agent" };

        // Act
        var definition = tool.CreateToolDefinition(context, _mockLogger.Object);

        // Assert
        definition.ShouldNotBeNull();
        definition.Name.ShouldBe("weather");
        definition.Description.ShouldBe("Get weather information for a location");
        definition.Category.ShouldBe(ToolCategory.Information);
        definition.Version.ShouldBe("1.0.0");
        definition.Parameters.ShouldNotBeNull();
        definition.Parameters.Items.ShouldContainKey("location");
        definition.RequiresConfirmation.ShouldBeTrue();
        definition.IsDangerous.ShouldBeFalse();
    }

    [Fact]
    [DisplayName("Tool ExecuteAsync with timeout should respect the limit")]
    public async Task Tool_ExecuteAsync_WithTimeout_ShouldRespectLimit()
    {
        // Arrange
        var tool = new SlowTool();
        var parameters = new Dictionary<string, object> { ["delay"] = 5000 };
        var context = new ToolContext { AgentId = "test" };

        using var cts = new CancellationTokenSource(100); // 100ms timeout

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await tool.ExecuteAsync(parameters, context, _mockLogger.Object, cts.Token));
    }

    [Fact]
    [DisplayName("Tool CreateParameters should define correct parameter types")]
    public void Tool_CreateParameters_ShouldDefineCorrectTypes()
    {
        // Arrange
        var tool = new TestCalculatorTool();

        // Act
        var parameters = tool.CreateParameters();

        // Assert
        parameters.ShouldNotBeNull();
        parameters.Items.Count.ShouldBe(3);

        parameters.Items["operation"].Type.ShouldBe("string");
        parameters.Items["operation"].Enum.ShouldContain("add");
        parameters.Items["operation"].Enum.ShouldContain("subtract");

        parameters.Items["a"].Type.ShouldBe("number");
        parameters.Items["b"].Type.ShouldBe("number");

        parameters.Required.ShouldContain("operation");
        parameters.Required.ShouldContain("a");
        parameters.Required.ShouldContain("b");
    }

    #endregion

    #region IAevatarToolManager Tests

    [Fact]
    [DisplayName("ToolManager RegisterToolAsync should add tool successfully")]
    public async Task ToolManager_RegisterToolAsync_ShouldAddTool()
    {
        // Arrange
        var manager = new MockToolManager();
        var toolDef = CreateTestToolDefinition("test-tool");

        // Act
        await manager.RegisterToolAsync(toolDef);
        var tools = await manager.GetAvailableToolsAsync();

        // Assert
        tools.ShouldContain(t => t.Name == "test-tool");
    }

    [Fact]
    [DisplayName("ToolManager RegisterToolAsync with duplicate name should handle correctly")]
    public async Task ToolManager_RegisterToolAsync_DuplicateName_ShouldHandleCorrectly()
    {
        // Arrange
        var manager = new MockToolManager();
        var toolDef1 = CreateTestToolDefinition("duplicate");
        var toolDef2 = CreateTestToolDefinition("duplicate");
        toolDef2.Description = "Updated description";

        // Act
        await manager.RegisterToolAsync(toolDef1);
        await manager.RegisterToolAsync(toolDef2); // Should replace

        var tools = await manager.GetAvailableToolsAsync();

        // Assert
        tools.Count(t => t.Name == "duplicate").ShouldBe(1);
        tools.First(t => t.Name == "duplicate").Description.ShouldBe("Updated description");
    }

    [Fact]
    [DisplayName("ToolManager ExecuteToolAsync with non-existent tool should return error")]
    public async Task ToolManager_ExecuteToolAsync_NonExistent_ShouldReturnError()
    {
        // Arrange
        var manager = new MockToolManager();

        // Act
        var result = await manager.ExecuteToolAsync(
            "non-existent-tool",
            new Dictionary<string, object>());

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("not found");
        result.ToolName.ShouldBe("non-existent-tool");
    }

    [Fact]
    [DisplayName("ToolManager ExecuteToolAsync with disabled tool should fail")]
    public async Task ToolManager_ExecuteToolAsync_DisabledTool_ShouldFail()
    {
        // Arrange
        var manager = new MockToolManager();
        var toolDef = CreateTestToolDefinition("disabled-tool");
        toolDef.IsEnabled = false;

        await manager.RegisterToolAsync(toolDef);

        // Act
        var result = await manager.ExecuteToolAsync(
            "disabled-tool",
            new Dictionary<string, object>());

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("disabled");
    }

    [Fact]
    [DisplayName("ToolManager GetAvailableToolsAsync should return only enabled tools")]
    public async Task ToolManager_GetAvailableToolsAsync_ShouldReturnOnlyEnabled()
    {
        // Arrange
        var manager = new MockToolManager();

        var enabledTool = CreateTestToolDefinition("enabled");
        enabledTool.IsEnabled = true;

        var disabledTool = CreateTestToolDefinition("disabled");
        disabledTool.IsEnabled = false;

        await manager.RegisterToolAsync(enabledTool);
        await manager.RegisterToolAsync(disabledTool);

        // Act
        var tools = await manager.GetAvailableToolsAsync();

        // Assert
        tools.Count.ShouldBe(1);
        tools[0].Name.ShouldBe("enabled");
    }

    [Fact]
    [DisplayName("ToolManager GenerateFunctionDefinitionsAsync should map tools correctly")]
    public async Task ToolManager_GenerateFunctionDefinitionsAsync_ShouldMapCorrectly()
    {
        // Arrange
        var manager = new MockToolManager();

        for (int i = 0; i < 3; i++)
        {
            await manager.RegisterToolAsync(CreateTestToolDefinition($"tool-{i}"));
        }

        // Act
        var functions = await manager.GenerateFunctionDefinitionsAsync();

        // Assert
        functions.ShouldNotBeEmpty();
        functions.Count.ShouldBe(3);

        foreach (var function in functions)
        {
            function.Name.ShouldNotBeEmpty();
            function.Description.ShouldNotBeEmpty();
            function.Parameters.ShouldNotBeNull();
        }
    }

    [Fact]
    [DisplayName("ToolManager ExecuteToolAsync should track execution history")]
    public async Task ToolManager_ExecuteToolAsync_ShouldTrackHistory()
    {
        // Arrange
        var manager = new MockToolManager();
        var toolDef = CreateTestToolDefinition("tracked-tool");
        await manager.RegisterToolAsync(toolDef);

        var params1 = new Dictionary<string, object> { ["param1"] = "value1" };
        var params2 = new Dictionary<string, object> { ["param2"] = "value2" };

        // Act
        await manager.ExecuteToolAsync("tracked-tool", params1);
        await manager.ExecuteToolAsync("tracked-tool", params2);

        // Assert
        manager.ExecutionHistory.Count.ShouldBe(2);
        manager.ExecutionHistory[0].toolName.ShouldBe("tracked-tool");
        manager.ExecutionHistory[0].parameters["param1"].ShouldBe("value1");
        manager.ExecutionHistory[1].parameters["param2"].ShouldBe("value2");
    }

    #endregion

    #region Helper Methods

    private static ToolDefinition CreateTestToolDefinition(string name)
    {
        return new ToolDefinition
        {
            Name = name,
            Description = $"Test tool {name}",
            Category = ToolCategory.Custom,
            Version = "1.0.0",
            IsEnabled = true,
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["param1"] = new() { Type = "string", Description = "First parameter" }
                },
                Required = new List<string> { "param1" }
            },
            ExecuteAsync = (parameters, context, ct) => Task.FromResult<IMessage>(new StringValue { Value = "success" })
        };
    }

    #endregion
}

/// <summary>
/// Test calculator tool implementation
/// </summary>
public class TestCalculatorTool : AevatarToolBase
{
    public override string Name => "calculator";
    public override string Description => "Perform basic arithmetic operations";
    public override ToolCategory Category => ToolCategory.Utility;
    
    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["operation"] = new()
                {
                    Type = "string",
                    Description = "The operation to perform",
                    Enum = new List<string> { "add", "subtract", "multiply", "divide" }.Cast<object>().ToList()
                },
                ["a"] = new()
                {
                    Type = "number",
                    Description = "First operand"
                },
                ["b"] = new()
                {
                    Type = "number",
                    Description = "Second operand"
                }
            },
            Required = new List<string> { "operation", "a", "b" }
        };
    }
    
    public override Task<IMessage?> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var operation = parameters["operation"].ToString();
        var a = Convert.ToDouble(parameters["a"]);
        var b = Convert.ToDouble(parameters["b"]);
        
        var result = operation switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => b != 0 ? a / b : throw new DivideByZeroException(),
            _ => throw new ArgumentException($"Unknown operation: {operation}")
        };

        return Task.FromResult<IMessage>(new Value { NumberValue = result })!;
    }
}

/// <summary>
/// Test weather tool implementation
/// </summary>
public class TestWeatherTool : AevatarToolBase
{
    public override string Name => "weather";
    public override string Description => "Get weather information for a location";
    public override ToolCategory Category => ToolCategory.Information;
    
    protected override bool RequiresConfirmation() => true;
    
    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["location"] = new()
                {
                    Type = "string",
                    Description = "The location to get weather for"
                },
                ["units"] = new()
                {
                    Type = "string",
                    Description = "Temperature units",
                    Enum = new List<string> { "celsius", "fahrenheit" }.Cast<object>().ToList(),
                    DefaultValue = "celsius"
                }
            },
            Required = new List<string> { "location" }
        };
    }
    
    public override Task<IMessage?> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var location = parameters["location"].ToString();
        var units = parameters.ContainsKey("units") ? parameters["units"].ToString() : "celsius";
        
        // Mock weather response
        var result = new Struct();
        result.Fields.Add("location", Value.ForString(location));
        result.Fields.Add("temperature", Value.ForNumber(22));
        result.Fields.Add("units", Value.ForString(units));
        result.Fields.Add("condition", Value.ForString("Sunny"));
        result.Fields.Add("humidity", Value.ForNumber(65));
        
        return Task.FromResult<IMessage>(result)!;
    }
}

/// <summary>
/// Test slow tool for timeout testing
/// </summary>
public class SlowTool : AevatarToolBase
{
    public override string Name => "slow";
    public override string Description => "A slow tool for testing timeouts";

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["delay"] = new() { Type = "integer", Description = "Delay in milliseconds" }
            },
            Required = new List<string> { "delay" }
        };
    }

    public override async Task<IMessage?> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var delay = Convert.ToInt32(parameters["delay"]);
        await Task.Delay(delay, cancellationToken);
        return new StringValue { Value = "completed" };
    }
}