using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Http;

namespace Aevatar.Agents.AI.Tool.Tools.BuiltIn;

public sealed class BingWebSearchProvider : IAevatarWebSearchProvider
{
    private readonly BingWebSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _httpClientName;

    public BingWebSearchProvider(
        BingWebSearchOptions options,
        IHttpClientFactory httpClientFactory,
        string httpClientName = WebSearchHttpClientNames.Bing)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _httpClientName = string.IsNullOrWhiteSpace(httpClientName) ? WebSearchHttpClientNames.Default : httpClientName;
    }

    public string Name => "bing";

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
                Error = "bing apiKey is not configured"
            };
        }

        var maxResults = Math.Clamp(request.MaxResults, 1, 10);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(_options.TimeoutMs, 500, 300_000)));

        var url = BuildUrl(_options.Endpoint, query, maxResults);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _options.ApiKey);

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

            // Bing shape: { webPages: { value: [ { name, url, snippet } ] } }
            if (!root.TryGetProperty("webPages", out var webPagesEl) || webPagesEl.ValueKind != JsonValueKind.Object)
            {
                return new WebSearchResponse
                {
                    Provider = Name,
                    Results = Array.Empty<WebSearchResultItem>(),
                    HttpStatus = status,
                    Error = "provider response missing 'webPages' object"
                };
            }

            if (!webPagesEl.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.Array)
            {
                return new WebSearchResponse
                {
                    Provider = Name,
                    Results = Array.Empty<WebSearchResultItem>(),
                    HttpStatus = status,
                    Error = "provider response missing 'webPages.value' array"
                };
            }

            var results = new List<WebSearchResultItem>(capacity: Math.Min(maxResults, 10));
            foreach (var el in valueEl.EnumerateArray())
            {
                if (results.Count >= maxResults)
                    break;

                var title = GetString(el, "name");
                var urlStr = GetString(el, "url");
                var snippet = GetString(el, "snippet");

                if (string.IsNullOrWhiteSpace(urlStr))
                    continue;

                results.Add(new WebSearchResultItem
                {
                    Title = WebSearchText.Truncate(title, 200),
                    Url = urlStr.Trim(),
                    Snippet = WebSearchText.Truncate(WebSearchText.NormalizeWhitespace(snippet), 600),
                    Score = null
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

    private static string BuildUrl(string endpoint, string query, int count)
    {
        var baseUrl = string.IsNullOrWhiteSpace(endpoint) ? BingWebSearchOptions.DefaultEndpoint : endpoint.Trim();
        var q = Uri.EscapeDataString(query);
        var suffix = $"q={q}&count={count}&responseFilter=Webpages";
        return baseUrl.Contains('?', StringComparison.Ordinal)
            ? $"{baseUrl}&{suffix}"
            : $"{baseUrl}?{suffix}";
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
}


