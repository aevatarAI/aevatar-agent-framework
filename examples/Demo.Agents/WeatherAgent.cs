using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

// WeatherAgentState is defined in demo_messages.proto

/// <summary>
/// Example: Weather query agent
/// </summary>
public class WeatherAgent : GAgentBase<WeatherAgentState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Weather Agent - Provides weather information for cities");
    }

    /// <summary>
    /// Query weather
    /// </summary>
    public async Task<string> GetWeatherAsync(string city, CancellationToken ct = default)
    {
        // Update state
        State.Location = city;
        State.UpdateCount++;
        State.LastUpdate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        // Simulate weather query
        var weather = GenerateWeather(city);
        
        // Update weather state
        var parts = weather.Split(',');
        if (parts.Length >= 2)
        {
            State.Condition = parts[0].Trim();
            if (double.TryParse(parts[1].Replace("°C", "").Trim(), out var temp))
            {
                State.Temperature = temp;
            }
        }

        Console.WriteLine($"[WeatherAgent] Weather query for city {city}: {weather}");

        return weather;
    }

    /// <summary>
    /// Get query statistics
    /// </summary>
    public int GetQueryCount() => State.UpdateCount;

    private string GenerateWeather(string city)
    {
        var weathers = new[] { "Sunny", "Cloudy", "Overcast", "Light Rain", "Heavy Rain", "Snow" };
        var random = new Random(city.GetHashCode());
        var temp = random.Next(-10, 35);
        var weather = weathers[random.Next(weathers.Length)];
        return $"{weather}, {temp}°C";
    }
}
