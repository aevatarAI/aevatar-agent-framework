using System.Text.RegularExpressions;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Agents.AI.Core;

public readonly record struct EventRoute(string? EventType, string? StepType, string TargetModule)
{
    public bool Matches(EventEnvelope envelope, IEventRouteEvaluator evaluator)
    {
        if (!string.IsNullOrWhiteSpace(EventType))
        {
            if (!evaluator.TryGetEventType(envelope, out var actualType))
                return false;
            if (!string.Equals(actualType, EventType, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(StepType))
        {
            if (!evaluator.TryGetStepType(envelope, out var actualStepType))
                return false;
            if (!string.Equals(actualStepType, StepType, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static EventRoute[] Parse(string? raw, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<EventRoute>();

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var list = deserializer.Deserialize<List<Dictionary<string, string>>>(raw);
            if (list != null && list.Count > 0)
                return FromYamlList(list, logger);
        }
        catch
        {
            // Fallback to line-based parsing.
        }

        return ParseLines(raw, logger);
    }

    private static EventRoute[] FromYamlList(
        List<Dictionary<string, string>> list,
        ILogger? logger)
    {
        var routes = new List<EventRoute>();
        foreach (var item in list)
        {
            if (!item.TryGetValue("when", out var whenRaw) ||
                !item.TryGetValue("to", out var toRaw))
            {
                continue;
            }

            if (TryParseWhen(whenRaw, out var eventType, out var stepType))
            {
                routes.Add(new EventRoute(eventType, stepType, toRaw.Trim()));
            }
            else
            {
                logger?.LogWarning("[EventRoute] Unsupported condition: {When}", whenRaw);
            }
        }

        return routes.ToArray();
    }

    private static EventRoute[] ParseLines(string raw, ILogger? logger)
    {
        var routes = new List<EventRoute>();
        var lines = raw.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var parts = trimmed.Split("->", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                continue;

            if (!TryParseWhen(parts[0], out var eventType, out var stepType))
            {
                logger?.LogWarning("[EventRoute] Unsupported condition: {When}", parts[0]);
                continue;
            }

            routes.Add(new EventRoute(eventType, stepType, parts[1].Trim()));
        }

        return routes.ToArray();
    }

    private static bool TryParseWhen(string whenRaw, out string? eventType, out string? stepType)
    {
        eventType = null;
        stepType = null;

        var input = whenRaw.Trim();

        var match = Regex.Match(input, @"event\.type\s*==?\s*[""']?(?<v>[^""']+)[""']?",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            eventType = match.Groups["v"].Value.Trim();
        }

        match = Regex.Match(input, @"event\.step_type\s*==?\s*[""']?(?<v>[^""']+)[""']?",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            stepType = match.Groups["v"].Value.Trim();
        }

        return !string.IsNullOrWhiteSpace(eventType) || !string.IsNullOrWhiteSpace(stepType);
    }
}
