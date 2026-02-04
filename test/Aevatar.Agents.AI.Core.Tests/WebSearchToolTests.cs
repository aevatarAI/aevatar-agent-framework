using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests;

public class WebSearchToolTests
{
    [Fact(DisplayName = "WebSearchTool returns provider results")]
    public async Task WebSearchTool_ShouldReturnProviderResults()
    {
        var provider = new FakeProvider();
        var tool = new WebSearchTool(provider, NullLogger<WebSearchTool>.Instance);
        var ctx = new ToolContext { AgentId = "a", AgentType = "t" };

        var msg = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "test query",
                ["maxResults"] = 2
            },
            ctx,
            NullLogger.Instance);

        msg.ShouldBeOfType<Struct>();

        var json = JsonFormatter.Default.Format(msg);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("provider").GetString().ShouldBe("fake");
        doc.RootElement.GetProperty("count").GetInt32().ShouldBe(2);

        var results = doc.RootElement.GetProperty("results");
        results.ValueKind.ShouldBe(JsonValueKind.Array);
        results.GetArrayLength().ShouldBe(2);
        results[0].GetProperty("url").GetString().ShouldBe("https://example.com/1");
    }

    [Fact(DisplayName = "WebSearchTool clamps maxResults and truncates long query")]
    public async Task WebSearchTool_ShouldClampMaxResults_AndTruncateQuery()
    {
        var provider = new CapturingProvider();
        var tool = new WebSearchTool(provider, NullLogger<WebSearchTool>.Instance);
        var ctx = new ToolContext { AgentId = "a", AgentType = "t" };

        var longQuery = new string('a', 600);
        var msg = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = longQuery,
                ["maxResults"] = 999
            },
            ctx,
            NullLogger.Instance);

        msg.ShouldBeOfType<Struct>();
        provider.Captured!.MaxResults.ShouldBe(10);
        provider.Captured!.Query.Length.ShouldBe(512);
    }

    [Fact(DisplayName = "WebSearchTool returns error when query is missing or empty")]
    public async Task WebSearchTool_ShouldReturnError_WhenQueryMissingOrEmpty()
    {
        var provider = new FakeProvider();
        var tool = new WebSearchTool(provider, NullLogger<WebSearchTool>.Instance);
        var ctx = new ToolContext { AgentId = "a", AgentType = "t" };

        var msg = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = ""
            },
            ctx,
            NullLogger.Instance);

        var json = JsonFormatter.Default.Format(msg);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("errors").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    private sealed class FakeProvider : IAevatarWebSearchProvider
    {
        public string Name => "fake";

        public Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
        {
            request.Query.ShouldBe("test query");
            request.MaxResults.ShouldBe(2);

            return Task.FromResult(new WebSearchResponse
            {
                Provider = Name,
                Results = new[]
                {
                    new WebSearchResultItem
                    {
                        Title = "T1",
                        Url = "https://example.com/1",
                        Snippet = "S1",
                        Score = 0.9
                    },
                    new WebSearchResultItem
                    {
                        Title = "T2",
                        Url = "https://example.com/2",
                        Snippet = "S2",
                        Score = 0.8
                    }
                }
            });
        }
    }

    private sealed class CapturingProvider : IAevatarWebSearchProvider
    {
        public string Name => "capture";

        public WebSearchRequest? Captured { get; private set; }

        public Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
        {
            Captured = request;
            return Task.FromResult(new WebSearchResponse
            {
                Provider = Name,
                Results = Array.Empty<WebSearchResultItem>()
            });
        }
    }
}


