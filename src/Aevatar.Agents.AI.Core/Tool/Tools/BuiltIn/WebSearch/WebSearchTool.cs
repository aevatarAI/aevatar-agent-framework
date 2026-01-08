using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tool.Tools.BuiltIn;

public sealed class WebSearchTool : AevatarToolBase
{
    private readonly IAevatarWebSearchProvider _provider;
    private readonly ILogger<WebSearchTool> _logger;

    // Token safety bounds
    private const int MaxQueryChars = 512;
    private const int DefaultMaxResults = 5;

    public WebSearchTool(IAevatarWebSearchProvider provider, ILogger<WebSearchTool> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "web_search";

    public override string Description =>
        "Search the web via a configured third-party provider. Returns {provider, query, count, results[{title,url,snippet,score}]}.";

    public override ToolCategory Category => ToolCategory.Information;

    public override string Version => "1.0.0";

    public override IList<string> Tags => new List<string> { "web", "search", "internet", "retrieval" };

    protected override bool RequiresConfirmation() => true;

    protected override int? GetRateLimit() => 60; // 60 searches/min default safety cap

    protected override TimeSpan? GetTimeout() => TimeSpan.FromSeconds(20);

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "query" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["query"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Search query. Keep it short and specific; do NOT include secrets.",
                    Required = true,
                    MinLength = 1,
                    MaxLength = MaxQueryChars
                },
                ["maxResults"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Maximum number of results to return (1-10).",
                    Required = false,
                    DefaultValue = DefaultMaxResults,
                    Minimum = 1,
                    Maximum = 10
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateParameters(ToNullableParameters(parameters));
        if (!validation.IsValid)
        {
            var errorResult = new { success = false, errors = validation.Errors };
            return ToStruct(errorResult);
        }

        var q = (parameters.GetValueOrDefault("query")?.ToString() ?? string.Empty).Trim();
        if (q.Length > MaxQueryChars)
            q = q[..MaxQueryChars];

        var maxResults = ClampInt(parameters.GetValueOrDefault("maxResults"), DefaultMaxResults, 1, 10);

        _logger.LogInformation("web_search via {Provider}: queryLen={Len} maxResults={Max}",
            _provider.Name, q.Length, maxResults);

        var resp = await _provider.SearchAsync(
            new WebSearchRequest { Query = q, MaxResults = maxResults },
            cancellationToken);

        var ok = string.IsNullOrWhiteSpace(resp.Error);
        var resultObj = new
        {
            success = ok,
            provider = resp.Provider,
            query = q,
            count = resp.Results?.Count ?? 0,
            httpStatus = resp.HttpStatus,
            error = resp.Error,
            results = resp.Results?.Select(r => new
            {
                title = r.Title,
                url = r.Url,
                snippet = r.Snippet,
                score = r.Score
            })
        };

        return ToStruct(resultObj);
    }

    public override ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        var result = base.ValidateParameters(parameters);

        if (parameters.TryGetValue("query", out var qObj))
        {
            var q = (qObj?.ToString() ?? string.Empty).Trim();
            if (q.Length == 0)
            {
                result.IsValid = false;
                result.Errors.Add("Required parameter 'query' is missing or empty");
            }

            if (q.Length > MaxQueryChars)
            {
                // Don't fail; clamp later.
                result.Warnings.Add($"Parameter 'query' will be truncated to {MaxQueryChars} chars");
            }
        }

        return result;
    }

    private static int ClampInt(object? v, int fallback, int min, int max)
    {
        if (!TryParseInt(v, out var i))
            i = fallback;
        return Math.Clamp(i, min, max);
    }

    private static bool TryParseInt(object? v, out int i)
    {
        i = 0;
        if (v == null) return false;
        if (v is int ii) { i = ii; return true; }
        if (v is long ll) { i = (int)Math.Clamp(ll, int.MinValue, int.MaxValue); return true; }
        return int.TryParse(v.ToString(), out i);
    }

    private static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonParser.Default.Parse<Struct>(json);
    }
}


