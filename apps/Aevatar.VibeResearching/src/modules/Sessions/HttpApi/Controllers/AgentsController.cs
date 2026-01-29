using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Sessions.Services;

namespace Aevatar.VibeResearching.Sessions.Controllers;

/// <summary>
/// Agent state and history management controller.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}/agents")]
[AllowAnonymous]
public class AgentsController : AbpControllerBase
{
    private readonly ResearchSessionManager _sessionManager;

    public AgentsController(ResearchSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Gets all agents for a session.
    /// GET /api/sessions/{sessionId}/agents
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAgentsAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        await Task.CompletedTask;

        if (!_sessionManager.TryGet(sessionId, out var session))
        {
            return NotFound(new { error = "session not found" });
        }

        // Return deterministic agent roster based on session ID (same as SSE bootstrap)
        var baseId = $"sra-{session.Id}";
        var roster = new[]
        {
            new { agent = "research_assistant", agentId = $"{baseId}-research_assistant" },
            new { agent = "planner", agentId = $"{baseId}-planner" },
            new { agent = "reasoner", agentId = $"{baseId}-reasoner" },
            new { agent = "librarian", agentId = $"{baseId}-librarian" },
            new { agent = "verifier", agentId = $"{baseId}-verifier" },
            new { agent = "dag_builder", agentId = $"{baseId}-dag_builder" },
            new { agent = "paper_editor", agentId = $"{baseId}-paper_editor" }
        };

        return Ok(new
        {
            sessionId = session.Id,
            coordinatorId = $"{baseId}-research_assistant",
            workerIds = new[]
            {
                $"{baseId}-planner",
                $"{baseId}-reasoner",
                $"{baseId}-librarian",
                $"{baseId}-verifier",
                $"{baseId}-dag_builder",
                $"{baseId}-paper_editor"
            },
            agentIds = roster.Select(r => r.agentId).ToArray(),
            agents = roster
        });
    }

    /// <summary>
    /// Gets agent states for all agents in a session.
    /// GET /api/sessions/{sessionId}/agents/states
    /// </summary>
    [HttpGet("states")]
    public async Task<IActionResult> GetAgentStatesAsync(
        [FromRoute] string sessionId,
        [FromQuery] int? historyLimit,
        [FromQuery] bool? includeHistory,
        CancellationToken ct = default)
    {
        await Task.CompletedTask;

        if (!_sessionManager.TryGet(sessionId, out var session))
        {
            return NotFound(new { error = "session not found" });
        }

        // Return workspace state snapshot
        var workspace = session.Workspace;

        return Ok(new
        {
            sessionId = session.Id,
            agents = Array.Empty<object>(), // Agent states would require deep integration with runtime
            workspace = new
            {
                kind = workspace.Kind,
                sessionId = workspace.SessionId,
                knowledge = new
                {
                    factsCount = workspace.Knowledge.FactsCount,
                    factsProposedCount = workspace.Knowledge.FactsProposedCount,
                    factsProposedRecent = workspace.Knowledge.FactsProposedRecent
                },
                materials = new
                {
                    rootDir = workspace.Materials.RootDir,
                    loadedAt = workspace.Materials.LoadedAt,
                    items = workspace.Materials.Items,
                    contextPreview = workspace.Materials.ContextPreview
                },
                vibe = workspace.Vibe
            }
        });
    }

    /// <summary>
    /// Gets history for a specific agent.
    /// GET /api/sessions/{sessionId}/agents/{agentId}/history
    /// </summary>
    [HttpGet("{agentId}/history")]
    public async Task<IActionResult> GetAgentHistoryAsync(
        [FromRoute] string sessionId,
        [FromRoute] string agentId,
        [FromQuery] int? limit,
        CancellationToken ct = default)
    {
        await Task.CompletedTask;

        if (!_sessionManager.TryGet(sessionId, out var session))
        {
            return NotFound(new { error = "session not found" });
        }

        // Return message snapshot from session
        var maxMessages = limit ?? 50;
        var messages = session.GetMessagesSnapshot(maxMessages);

        return Ok(new
        {
            sessionId = session.Id,
            agentId,
            messages
        });
    }
}
