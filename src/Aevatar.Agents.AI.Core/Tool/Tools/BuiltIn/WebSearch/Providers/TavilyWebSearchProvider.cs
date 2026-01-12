using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Http;

namespace Aevatar.Agents.AI.Tool.Tools.BuiltIn;

public sealed class TavilyWebSearchProvider : IAevatarWebSearchProvider
{
    private readonly TavilyWebSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _httpClientName;

    public TavilyWebSearchProvider(
        TavilyWebSearchOptions options,
        IHttpClientFactory httpClientFactory,
        string httpClientName = WebSearchHttpClientNames.Tavily)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _httpClientName = string.IsNullOrWhiteSpace(httpClientName) ? WebSearchHttpClientNames.Default : httpClientName;
    }

    public string Name => "tavily";

    public async Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = (request.Query ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return new WebSearchResponse
            {
                Provider = Name,
                Results = Array.Empty<WebSearchResultItem>(),
                Error = "query is required"
            };
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new WebSearchResponse
            {
                Provider = Name,
                Results = Array.Empty<WebSearchResultItem>(),
                Error = "tavily apiKey is not configured"
            };
        }

        var maxResults = Math.Clamp(request.MaxResults, 1, 10);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(_options.TimeoutMs, 500, 300_000)));

        var payload = new
        {
            api_key = _options.ApiKey,
            query = query,
            max_results = maxResults,
            search_depth = string.IsNullOrWhiteSpace(_options.SearchDepth) ? "basic" : _options.SearchDepth,
            include_answer = false,
            include_raw_content = false,
            include_images = false
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var http = _httpClientFactory.CreateClient(_httpClientName);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return new WebSearchResponse
            {
                Provider = Name,
                Results = Array.Empty<WebSearchResultItem>(),
                Error = "web_search timed out"
            };
        }
        catch (Exception ex)
        {
            return new WebSearchResponse
            {
                Provider = Name,
                Results = Array.Empty<WebSearchResultItem>(),
                Error = $"web_search request failed: {ex.Message}"
            };
        }

        var status = (int)resp.StatusCode;
        string body;
        try
        {
            body = await resp.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex)
        {
            return new WebSearchResponse
            {
                Provider = Name,
                Results = Array.Empty<WebSearchResultItem>(),
                HttpStatus = status,
                Error = $"failed to read response: {ex.Message}"
            };
        }

        if (!resp.IsSuccessStatusCode)
        {
            return new WebSearchResponse
            {
                Provider = Name,
                Results = Array.Empty<WebSearchResultItem>(),
                HttpStatus = status,
                Error = $"provider returned HTTP {status}: {WebSearchText.Truncate(body, 4000)}"
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
            {
                return new WebSearchResponse
                {
                    Provider = Name,
                    Results = Array.Empty<WebSearchResultItem>(),
                    HttpStatus = status,
                    Error = "provider response missing 'results' array"
                };
            }

            var results = new List<WebSearchResultItem>(capacity: Math.Min(maxResults, 10));
            foreach (var el in resultsEl.EnumerateArray())
            {
                if (results.Count >= maxResults)
                    break;

                var title = GetString(el, "title");
                var url = GetString(el, "url");
                var content = GetString(el, "content");
                var score = GetDouble(el, "score");

                if (string.IsNullOrWhiteSpace(url))
                    continue;

                results.Add(new WebSearchResultItem
                {
                    Title = WebSearchText.Truncate(title, 200),
                    Url = url.Trim(),
                    Snippet = WebSearchText.Truncate(WebSearchText.NormalizeWhitespace(content), 600),
                    Score = score
                });
            }

            return new WebSearchResponse
            {
                Provider = Name,
                Results = results,
                HttpStatus = status
            };
        }
        catch (Exception ex)
        {
            return new WebSearchResponse
            {
                Provider = Name,
                Results = Array.Empty<WebSearchResultItem>(),
                HttpStatus = status,
                Error = $"failed to parse provider response: {ex.Message}"
            };
        }
    }

    private static string GetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (!el.TryGetProperty(name, out var v))
            return string.Empty;

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? string.Empty,
            _ => v.ToString()
        };
    }

    private static double? GetDouble(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        if (!el.TryGetProperty(name, out var v))
            return null;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
            return d;

        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var ds))
            return ds;

        return null;
    }
}


