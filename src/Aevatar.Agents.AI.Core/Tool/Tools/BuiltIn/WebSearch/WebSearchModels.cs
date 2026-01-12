namespace Aevatar.Agents.AI.Tool.Tools.BuiltIn;

public sealed class WebSearchRequest
{
    public required string Query { get; init; }
    public int MaxResults { get; init; } = 5;
}

public sealed class WebSearchResponse
{
    public required string Provider { get; init; }
    public required IReadOnlyList<WebSearchResultItem> Results { get; init; }
    public string? Error { get; init; }
    public int? HttpStatus { get; init; }
}

public sealed class WebSearchResultItem
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Snippet { get; init; }
    public double? Score { get; init; }
}

public sealed class TavilyWebSearchOptions
{
    public const string DefaultEndpoint = "https://api.tavily.com/search";

    public string ApiKey { get; init; } = string.Empty;
    public string Endpoint { get; init; } = DefaultEndpoint;
    public int TimeoutMs { get; init; } = 15_000;
    public string SearchDepth { get; init; } = "basic"; // "basic" | "advanced" (provider-defined)
}

public sealed class BraveWebSearchOptions
{
    public const string DefaultEndpoint = "https://api.search.brave.com/res/v1/web/search";

    public string ApiKey { get; init; } = string.Empty; // X-Subscription-Token
    public string Endpoint { get; init; } = DefaultEndpoint;
    public int TimeoutMs { get; init; } = 15_000;
}

public sealed class BingWebSearchOptions
{
    public const string DefaultEndpoint = "https://api.bing.microsoft.com/v7.0/search";

    public string ApiKey { get; init; } = string.Empty; // Ocp-Apim-Subscription-Key
    public string Endpoint { get; init; } = DefaultEndpoint;
    public int TimeoutMs { get; init; } = 15_000;
}

public sealed class SerperWebSearchOptions
{
    public const string DefaultEndpoint = "https://google.serper.dev/search";

    public string ApiKey { get; init; } = string.Empty; // X-API-KEY
    public string Endpoint { get; init; } = DefaultEndpoint;
    public int TimeoutMs { get; init; } = 15_000;
}


