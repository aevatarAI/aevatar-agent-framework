using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace AIAgentWithToolDemo;

// ============================================
// Calculator Tool - Strongly-typed implementation
// ============================================

/// <summary>
/// Calculator tool input parameters
/// </summary>
public record CalculatorInput
{
    /// <summary>
    /// Operation type
    /// </summary>
    [Required(ErrorMessage = "operation parameter is required")]
    [Description("Operation type: add, subtract, multiply, divide")]
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }
    
    /// <summary>
    /// Numbers to operate on
    /// </summary>
    [Required(ErrorMessage = "numbers parameter is required")]
    [MinLength(2, ErrorMessage = "At least two numbers are required")]
    [Description("Array of numbers to operate on, must contain at least two numbers")]
    [JsonPropertyName("numbers")]
    public required double[] Numbers { get; init; }
}

/// <summary>
/// Calculator tool
/// Uses strongly-typed parameters with automatic validation and conversion
/// </summary>
public class CalculatorTool : TypedAevatarToolBase<CalculatorInput, Struct>
{
    public override string Name => "calculator";
    
    public override string Description => "Perform mathematical calculations including addition, subtraction, multiplication, and division";
    
    public override ToolCategory Category => ToolCategory.Utility;

    protected override async Task<Struct> ExecuteTypedAsync(
        CalculatorInput input,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        // Directly use strongly-typed parameters - no need to handle JsonElement or Dictionary!
        var a = input.Numbers[0];
        var b = input.Numbers[1];
        
        var result = input.Operation.ToLower() switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => b != 0 ? a / b : throw new InvalidOperationException("Division by zero"),
            _ => throw new ArgumentException($"Unsupported operation: {input.Operation}")
        };
        
        logger?.LogInformation("🧮 Calculator executed: {A} {Op} {B} = {Result}", 
            a, input.Operation, b, result);
        
        await Task.CompletedTask;
        
        // Return Protobuf message
        return new Struct
        {
            Fields =
            {
                ["result"] = Value.ForNumber(result),
                ["expression"] = Value.ForString($"{a} {input.Operation} {b} = {result}")
            }
        };
    }
}

// ============================================
// Weather Tool - Strongly-typed implementation
// ============================================

/// <summary>
/// Weather query tool input parameters
/// </summary>
public record WeatherInput
{
    /// <summary>
    /// City name to query
    /// </summary>
    [Required(ErrorMessage = "city parameter is required")]
    [Description("City name to query weather for, e.g.: Beijing, Shanghai, Shenzhen")]
    [JsonPropertyName("city")]
    public required string City { get; init; }
}

/// <summary>
/// Weather query tool (simulated)
/// Demonstrates how to use strongly-typed parameters
/// </summary>
public class WeatherTool : TypedAevatarToolBase<WeatherInput, Struct>
{
    private static readonly Random Random = new();
    
    private static readonly string[] Conditions = 
    { 
        "Sunny", "Cloudy", "Overcast", "Light Rain", "Moderate Rain", "Heavy Rain", "Thunderstorm", "Snow" 
    };

    public override string Name => "get_weather";
    
    public override string Description => "Get weather information for a specified city (simulated data)";
    
    public override ToolCategory Category => ToolCategory.Information;

    protected override async Task<Struct> ExecuteTypedAsync(
        WeatherInput input,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        // Simulate weather data
        var temperature = Random.Next(15, 35);
        var condition = Conditions[Random.Next(Conditions.Length)];
        
        logger?.LogInformation("🌤️ Weather query: {City} - {Temp}°C, {Condition}", 
            input.City, temperature, condition);
        
        await Task.CompletedTask;
        
        // Return structured weather data
        return new Struct
        {
            Fields =
            {
                ["city"] = Value.ForString(input.City),
                ["temperature"] = Value.ForNumber(temperature),
                ["condition"] = Value.ForString(condition),
                ["message"] = Value.ForString($"{input.City} weather: {condition}, {temperature}°C")
            }
        };
    }
}
