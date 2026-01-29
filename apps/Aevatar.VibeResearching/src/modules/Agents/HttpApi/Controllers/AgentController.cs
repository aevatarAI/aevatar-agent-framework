using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Agents.Application.Contracts.DTOs;
using Aevatar.VibeResearching.Agents.Application.Contracts.Services;

namespace Aevatar.VibeResearching.Agents.Controllers;

/// <summary>
/// Agent provider mapping management controller.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}")]
[Authorize]
public class AgentController : AbpControllerBase
{
    private readonly IAgentAppService _agentAppService;

    public AgentController(IAgentAppService agentAppService)
    {
        _agentAppService = agentAppService;
    }

    /// <summary>
    /// Gets the agent-to-provider mapping.
    /// GET /api/sessions/{sessionId}/agent-providers
    /// </summary>
    [HttpGet("agent-providers")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProvidersAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var mapping = await _agentAppService.GetProvidersAsync(sessionId, ct);
        return Ok(mapping);
    }

    /// <summary>
    /// Maps an agent to a specific LLM provider.
    /// PUT /api/sessions/{sessionId}/agent-providers
    /// </summary>
    [HttpPut("agent-providers")]
    public async Task<IActionResult> UpsertProviderAsync(
        [FromRoute] string sessionId,
        [FromBody] AgentProviderDto input,
        CancellationToken ct = default)
    {
        await _agentAppService.UpsertProviderAsync(sessionId, input, ct);
        return Ok(new { ok = true, sessionId, agent = input.Agent });
    }

    /// <summary>
    /// Clears the provider mapping for an agent.
    /// DELETE /api/sessions/{sessionId}/agent-providers/{agentName}
    /// </summary>
    [HttpDelete("agent-providers/{agentName}")]
    public async Task<IActionResult> ClearProviderAsync(
        [FromRoute] string sessionId,
        [FromRoute] string agentName,
        CancellationToken ct = default)
    {
        await _agentAppService.ClearProviderAsync(sessionId, agentName, ct);
        return Ok(new { ok = true, sessionId, agent = agentName });
    }
}
