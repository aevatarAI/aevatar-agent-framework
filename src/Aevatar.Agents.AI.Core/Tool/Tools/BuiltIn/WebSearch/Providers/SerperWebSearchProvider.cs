using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Http;

namespace Aevatar.Agents.AI.Tool.Tools.BuiltIn;

public sealed class SerperWebSearchProvider : IAevatarWebSearchProvider
{
    private readonly SerperWebSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _httpClientName;

    public SerperWebSearchProvider(
        SerperWebSearchOptions options,
        IHttpClientFactory httpClientFactory,
        string httpClientName = WebSearchHttpClientNames.Serper)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _httpClientName = string.IsNullOrWhiteSpace(httpClientName) ? WebSearchHttpClientNames.Default : httpClientName;
    }

    public string Name => "serper";

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
                Error = "serper apiKey is not configured"
            };
        }

        var maxResults = Math.Clamp(request.MaxResults, 1, 10);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(_options.TimeoutMs, 500, 300_000)));

        var payload = new
        {
            q = query,
            num = maxResults
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("X-API-KEY", _options.ApiKey);
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

            // Serper shape: { organic: [ { title, link, snippet } ] }
            if (!root.TryGetProperty("organic", out var organicEl) || organicEl.ValueKind != JsonValueKind.Array)
            {
                return new WebSearchResponse
                {
                    Provider = Name,
                    Results = Array.Empty<WebSearchResultItem>(),
                    HttpStatus = status,
                    Error = "provider response missing 'organic' array"
                };
            }

            var results = new List<WebSearchResultItem>(capacity: Math.Min(maxResults, 10));
            foreach (var el in organicEl.EnumerateArray())
            {
                if (results.Count >= maxResults)
                    break;

                var title = GetString(el, "title");
                var urlStr = GetString(el, "link");
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


