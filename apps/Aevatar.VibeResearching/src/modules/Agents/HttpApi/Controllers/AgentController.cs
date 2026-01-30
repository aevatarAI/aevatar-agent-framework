using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Agents.Application.Contracts.DTOs;
using Aevatar.VibeResearching.Agents.Application.Contracts.Services;
using Aevatar.VibeResearching.Agents.Mesh;

namespace Aevatar.VibeResearching.Agents.Controllers;

/// <summary>
/// Agent provider mapping and mesh topology controller.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}")]
[Authorize]
public class AgentController : AbpControllerBase
{
    private readonly IAgentAppService _agentAppService;
    private readonly IMeshDefinitionStore _meshStore;
    private readonly IMeshCompilerService _compiler;
    private readonly IMeshExecutionPlanner _planner;

    public AgentController(
        IAgentAppService agentAppService,
        IMeshDefinitionStore meshStore,
        IMeshCompilerService compiler,
        IMeshExecutionPlanner planner)
    {
        _agentAppService = agentAppService;
        _meshStore = meshStore;
        _compiler = compiler;
        _planner = planner;
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

    /// <summary>
    /// Gets the mesh topology for visualization.
    /// Returns nodes and edges derived from the current mesh definition.
    /// GET /api/sessions/{sessionId}/mesh-topology
    /// </summary>
    [HttpGet("mesh-topology")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMeshTopologyAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        // Load raw mesh definition (YAML/JSON)
        var (raw, _) = await _meshStore.TryLoadRawAsync(sessionId, ct);
        if (string.IsNullOrWhiteSpace(raw))
            return NotFound(new { error = "mesh_not_found", message = "No mesh definition found for this session." });

        // Compile raw → MeshDefinition
        var compileResult = _compiler.Compile(raw);
        if (!compileResult.Ok || compileResult.Definition == null)
            return BadRequest(new { error = "mesh_compile_failed", errors = compileResult.Errors });

        // Plan → MeshExecutionPlan (for topology extraction)
        var planResult = _planner.Plan(sessionId, "topology-preview", compileResult.Definition);
        if (!planResult.Ok || planResult.Plan == null)
            return BadRequest(new { error = "mesh_plan_failed", errors = planResult.Errors });

        // Extract topology for visualization
        var topology = new
        {
            nodes = planResult.Plan.Nodes.Select(n => new { id = n.Id, type = n.Type }),
            edges = planResult.Plan.Nodes.SelectMany(n => n.Inbound.Select(b => new { from = b.FromNodeId, to = n.Id }))
        };

        return Ok(topology);
    }
}
