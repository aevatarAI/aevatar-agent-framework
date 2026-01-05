using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers;

/// <summary>
/// State Query Controller for CQRS read model
/// Provides endpoints to query agent states from Elasticsearch
/// </summary>
[Route("api/states")]
[ApiController]
public class StateQueryController : AbpControllerBase
{
    private readonly IStateQueryService _stateQueryService;
    private readonly ILogger<StateQueryController> _logger;

    public StateQueryController(
        IStateQueryService stateQueryService,
        ILogger<StateQueryController> logger)
    {
        _stateQueryService = stateQueryService;
        _logger = logger;
    }

    /// <summary>
    /// Get state by agent ID
    /// </summary>
    /// <param name="agentType">Agent type name</param>
    /// <param name="agentId">Agent ID</param>
    /// <returns>Agent state</returns>
    [HttpGet("{agentType}/{agentId}")]
    public async Task<ActionResult<StateQueryResult>> GetById(
        [FromRoute] string agentType,
        [FromRoute] string agentId)
    {
        _logger.LogInformation("📊 Querying state for agent {AgentType}/{AgentId}", agentType, agentId);

        try
        {
            var result = await _stateQueryService.GetByIdAsync(agentType, agentId);
            if (result == null)
            {
                return NotFound($"State not found for agent {agentType}/{agentId}");
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying state for agent {AgentType}/{AgentId}", agentType, agentId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Query states with Lucene query syntax
    /// </summary>
    /// <param name="request">Query request</param>
    /// <returns>Paged state results</returns>
    [HttpPost("query")]
    public async Task<ActionResult<PagedStateQueryResult>> Query([FromBody] StateQuery request)
    {
        _logger.LogInformation(
            "📊 Querying states for type {AgentType}, query: {Query}, page: {Page}",
            request.AgentType, request.QueryString ?? "*", request.PageIndex);

        try
        {
            var result = await _stateQueryService.QueryAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying states for type {AgentType}", request.AgentType);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Count states matching query
    /// </summary>
    /// <param name="agentType">Agent type name</param>
    /// <param name="queryString">Optional Lucene query string</param>
    /// <returns>Count of matching states</returns>
    [HttpGet("{agentType}/count")]
    public async Task<ActionResult<StateCountResponseDto>> Count(
        [FromRoute] string agentType,
        [FromQuery] string? queryString = null)
    {
        _logger.LogInformation("📊 Counting states for type {AgentType}, query: {Query}",
            agentType, queryString ?? "*");

        try
        {
            var count = await _stateQueryService.CountAsync(agentType, queryString);
            return Ok(new StateCountResponseDto { AgentType = agentType, Count = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting states for type {AgentType}", agentType);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

/// <summary>
/// State count response
/// </summary>
public class StateCountResponseDto
{
    /// <summary>
    /// Agent type
    /// </summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>
    /// Count of matching states
    /// </summary>
    public long Count { get; set; }
}

