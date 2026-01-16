using Microsoft.Extensions.Http;

namespace Aevatar.Agents.AI.Tool.Tools.BuiltIn;

public interface IAevatarWebSearchProvider
{
    string Name { get; }

    Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fallback factory used when host didn't register IHttpClientFactory (best-effort).
/// </summary>
internal sealed class FallbackHttpClientFactory : IHttpClientFactory
{
    private static readonly HttpClient Shared = new();

    public HttpClient CreateClient(string name) => Shared;
}


