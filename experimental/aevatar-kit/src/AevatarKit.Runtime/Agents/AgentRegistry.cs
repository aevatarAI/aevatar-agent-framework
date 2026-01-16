using System.Collections.Concurrent;
using AevatarKit;

namespace AevatarKit.Runtime.Agents;

public interface IAgentRegistry
{
    IReadOnlyList<AgentDefinition> List(string? query = null);
    IReadOnlyList<AgentCategory> ListCategories(string? query = null);
    AgentDefinition Create(CreateAgentRequest request);
}

/// <summary>
/// In-memory agent registry for MVP UI demo.
/// </summary>
public sealed class InMemoryAgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentDefinition> _agents = new();
    private readonly ConcurrentDictionary<string, string> _categoryDisplayNames = new();

    public InMemoryAgentRegistry()
    {
        // Seed: match the sample layout in mcp-agent-graph screenshots.
        SeedCategory("creator", "creator");
        SeedCategory("system_operations", "system_operations");

        SeedAgent("Agent Creator", "creator", "Create agents from templates.", toolCount: 6);
        SeedAgent("Graph Designer", "creator", "Design workflows/graphs.", toolCount: 4);
        SeedAgent("MCP Builder", "creator", "Register MCP servers & tools.", toolCount: 3);
        SeedAgent("Prompt Generator", "creator", "Manage and generate prompts.", toolCount: 2);

        SeedAgent("System Operations", "system_operations", "Safe system tooling & diagnostics.", toolCount: 5);
    }

    public IReadOnlyList<AgentDefinition> List(string? query = null)
    {
        var q = Normalize(query);
        var items = _agents.Values.ToList();

        if (string.IsNullOrWhiteSpace(q))
        {
            return items
                .OrderBy(a => a.CategoryId)
                .ThenBy(a => a.Name)
                .ToList();
        }

        return items
            .Where(a =>
                (a.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.CategoryId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(a => a.CategoryId)
            .ThenBy(a => a.Name)
            .ToList();
    }

    public IReadOnlyList<AgentCategory> ListCategories(string? query = null)
    {
        var agents = List(query);
        var groups = agents
            .GroupBy(a => a.CategoryId ?? "uncategorized")
            .OrderBy(g => g.Key);

        var categories = new List<AgentCategory>();
        foreach (var g in groups)
        {
            var categoryId = g.Key;
            categories.Add(new AgentCategory
            {
                CategoryId = categoryId,
                DisplayName = _categoryDisplayNames.TryGetValue(categoryId, out var dn) ? dn : categoryId,
                AgentCount = g.Count()
            });
        }

        return categories;
    }

    public AgentDefinition Create(CreateAgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("name is required");
        }

        var categoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? "creator" : request.CategoryId.Trim();
        SeedCategory(categoryId, categoryId);

        var agent = new AgentDefinition
        {
            AgentId = Guid.NewGuid().ToString("N"),
            Name = name,
            CategoryId = categoryId,
            Description = request.Description ?? string.Empty,
            ToolCount = 0
        };

        _agents[agent.AgentId] = agent;
        return agent;
    }

    private void SeedCategory(string categoryId, string displayName)
    {
        _categoryDisplayNames.TryAdd(categoryId, displayName);
    }

    private void SeedAgent(string name, string categoryId, string description, int toolCount)
    {
        var agent = new AgentDefinition
        {
            AgentId = Guid.NewGuid().ToString("N"),
            Name = name,
            CategoryId = categoryId,
            Description = description,
            ToolCount = toolCount
        };
        _agents[agent.AgentId] = agent;
    }

    private static string? Normalize(string? s)
    {
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}


