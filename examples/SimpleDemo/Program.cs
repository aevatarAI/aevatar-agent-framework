using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Demo.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.WriteLine("🌌 Aevatar Agent Framework - Simple Demo");
Console.WriteLine("=========================================\n");

// Setup dependency injection
var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
});

var serviceProvider = services.BuildServiceProvider();

// Get factory
var factory = serviceProvider.GetRequiredService<IGAgentActorFactory>();

Console.WriteLine("📊 Creating Calculator Agent...");
var calculatorId = Guid.NewGuid().ToString();
var calculatorActor = await factory.CreateGAgentActorAsync<CalculatorAgent>(calculatorId);
Console.WriteLine($"✅ Calculator Agent created successfully! ID: {calculatorActor.Id}\n");

// Get Agent through Actor and execute operations
var calculator = (CalculatorAgent)calculatorActor.GetAgent();

Console.WriteLine("🔢 Executing calculations:");
Console.WriteLine("─────────────────");

// Addition
var sum = await calculator.AddAsync(10, 5);
Console.WriteLine($"  10 + 5 = {sum}");

// Subtraction
var difference = await calculator.SubtractAsync(20, 8);
Console.WriteLine($"  20 - 8 = {difference}");

// Multiplication
var product = await calculator.MultiplyAsync(6, 7);
Console.WriteLine($"  6 × 7 = {product}");

// Division
var quotient = await calculator.DivideAsync(100, 4);
Console.WriteLine($"  100 ÷ 4 = {quotient}");

Console.WriteLine($"\n📝 Calculation history:");
var history = calculator.GetHistory();
foreach (var item in history)
{
    Console.WriteLine($"  {item}");
}

Console.WriteLine($"\n✨ Last result: {calculator.GetLastResult()}");
Console.WriteLine($"📈 Operation count: {calculator.GetState().OperationCount}");

// Test Weather Agent
Console.WriteLine("\n\n🌤️  Creating Weather Agent...");
var weatherId = Guid.NewGuid().ToString();
var weatherActor = await factory.CreateGAgentActorAsync<WeatherAgent>(weatherId);
Console.WriteLine($"✅ Weather Agent created successfully! ID: {weatherActor.Id}\n");

var weather = (WeatherAgent)weatherActor.GetAgent();

Console.WriteLine("🌍 Querying weather:");
Console.WriteLine("─────────────────");

var cities = new[] { "Beijing", "Shanghai", "Guangzhou", "Shenzhen" };
foreach (var city in cities)
{
    var weatherInfo = await weather.GetWeatherAsync(city);
    Console.WriteLine($"  {city}: {weatherInfo}");
}

Console.WriteLine($"\n📊 Query count: {weather.GetQueryCount()}");

// Cleanup
Console.WriteLine("\n\n🧹 Cleaning up resources...");
await calculatorActor.DeactivateAsync();
await weatherActor.DeactivateAsync();

Console.WriteLine("✅ Demo completed!");
Console.WriteLine("\nExample ran successfully!");

