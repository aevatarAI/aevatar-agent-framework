using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Exceptions;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Knowledge DAG and facts management controller.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}")]
[Authorize]
public class KnowledgeController : AbpControllerBase
{
    private readonly IKnowledgeAppService _knowledgeAppService;
    private readonly IDagStore _dagStore;
    private readonly IKnowledgeGraphClientFactory _graphClientFactory;

    public KnowledgeController(
        IKnowledgeAppService knowledgeAppService,
        IDagStore dagStore,
        IKnowledgeGraphClientFactory graphClientFactory)
    {
        _knowledgeAppService = knowledgeAppService;
        _dagStore = dagStore;
        _graphClientFactory = graphClientFactory;
    }

    /// <summary>
    /// Gets the current DAG snapshot for a session.
    /// GET /api/sessions/{sessionId}/dag
    /// </summary>
    [HttpGet("dag")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDagAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var dag = await _dagStore.GetSnapshotForListAsync("global", ct, currentSessionId: sessionId);
        return Ok(new { ok = true, sessionId, dag });
    }

    /// <summary>
    /// Creates a new fact proposal.
    /// POST /api/sessions/{sessionId}/facts
    /// </summary>
    [HttpPost("facts")]
    public async Task<IActionResult> SaveFactAsync(
        [FromRoute] string sessionId,
        [FromBody] SaveFactDto input,
        CancellationToken ct = default)
    {
        var proposal = await _knowledgeAppService.SaveFactAsync(sessionId, input, ct);
        return Ok(new
        {
            ok = true,
            sessionId,
            proposal = new { factId = proposal.FactId, title = proposal.Title }
        });
    }

    /// <summary>
    /// Records a vote on a fact proposal.
    /// POST /api/sessions/{sessionId}/facts/{factId}/votes
    /// </summary>
    [HttpPost("facts/{factId}/votes")]
    public async Task<IActionResult> VoteFactAsync(
        [FromRoute] string sessionId,
        [FromRoute] string factId,
        [FromBody] FactVoteDto input,
        CancellationToken ct = default)
    {
        await _knowledgeAppService.VoteFactAsync(sessionId, factId, input, ct);
        return Ok(new { ok = true, sessionId, factId });
    }

    /// <summary>
    /// Records a programmatic verification result for a fact.
    /// POST /api/sessions/{sessionId}/facts/{factId}/verifications
    /// </summary>
    [HttpPost("facts/{factId}/verifications")]
    public async Task<IActionResult> VerifyFactAsync(
        [FromRoute] string sessionId,
        [FromRoute] string factId,
        [FromBody] FactVerificationDto input,
        CancellationToken ct = default)
    {
        await _knowledgeAppService.VerifyFactAsync(sessionId, factId, input, ct);
        return Ok(new { ok = true, sessionId, factId });
    }

    /// <summary>
    /// Promotes a fact from proposal to the DAG.
    /// POST /api/sessions/{sessionId}/facts/{factId}/promote
    /// </summary>
    [HttpPost("facts/{factId}/promote")]
    public async Task<IActionResult> PromoteFactAsync(
        [FromRoute] string sessionId,
        [FromRoute] string factId,
        [FromBody] PromoteFactDto input,
        CancellationToken ct = default)
    {
        var decision = await _knowledgeAppService.PromoteFactAsync(sessionId, factId, input, ct);

        if (decision == null)
            return Ok(new { ok = true, sessionId, factId, pending = true });

        return Ok(new
        {
            ok = true,
            sessionId,
            factId,
            decision = decision.Decision.ToString(),
            basis = decision.Basis,
            rationale = decision.Rationale
        });
    }

    /// <summary>
    /// Explains a DAG node with dependency closure.
    /// GET /api/sessions/{sessionId}/dag/nodes/{nodeId}/explain
    /// </summary>
    [HttpGet("dag/nodes/{nodeId}/explain")]
    [AllowAnonymous]
    public async Task<IActionResult> ExplainNodeAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        CancellationToken ct = default)
    {
        var explain = await _knowledgeAppService.ExplainNodeAsync(sessionId, nodeId, ct);
        return Ok(new
        {
            ok = true,
            sessionId,
            nodeId,
            explain
        });
    }

    /// <summary>
    /// Explains a DAG node with dependency closure (alias route for frontend compatibility).
    /// GET /api/sessions/{sessionId}/dag/{nodeId}/explain
    /// </summary>
    [HttpGet("dag/{nodeId}/explain")]
    [AllowAnonymous]
    public async Task<IActionResult> ExplainDagNodeAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        CancellationToken ct = default)
    {
        var explain = await _knowledgeAppService.ExplainNodeAsync(sessionId, nodeId, ct);
        return Ok(new
        {
            ok = true,
            sessionId,
            nodeId,
            explain
        });
    }

    /// <summary>
    /// Explains a graph node including knowledge chain and relationships.
    /// GET /api/sessions/{sessionId}/graph/{nodeId}/explain
    /// </summary>
    [HttpGet("graph/{nodeId}/explain")]
    [AllowAnonymous]
    public async Task<IActionResult> ExplainGraphNodeAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        CancellationToken ct = default)
    {
        // Try session-scoped client first (for plan nodes)
        var client = _graphClientFactory.CreateClient(sessionId);
        try
        {
            var explanation = await client.ExplainNodeAsync(nodeId, ct);
            return Ok(new { ok = true, sessionId, nodeId, explanation });
        }
        catch (NodeNotFoundException) when (sessionId != "global")
        {
            // Knowledge nodes are stored with global scope — fall back
            var globalClient = _graphClientFactory.CreateClient("global");
            var explanation = await globalClient.ExplainNodeAsync(nodeId, ct);
            return Ok(new { ok = true, sessionId, nodeId, explanation });
        }
    }

    /// <summary>
    /// Gets the knowledge chain for a node (traverses all upstream dependencies).
    /// GET /api/sessions/{sessionId}/graph/{nodeId}/chain
    /// </summary>
    [HttpGet("graph/{nodeId}/chain")]
    [AllowAnonymous]
    public async Task<IActionResult> GetKnowledgeChainAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        CancellationToken ct = default)
    {
        var client = _graphClientFactory.CreateClient(sessionId);
        try
        {
            var chain = await client.GetKnowledgeChainDetailsAsync(nodeId, ct);
            return Ok(new { ok = true, sessionId, nodeId, chain });
        }
        catch (NodeNotFoundException) when (sessionId != "global")
        {
            var globalClient = _graphClientFactory.CreateClient("global");
            var chain = await globalClient.GetKnowledgeChainDetailsAsync(nodeId, ct);
            return Ok(new { ok = true, sessionId, nodeId, chain });
        }
    }

    /// <summary>
    /// Gets the session summary (plan progress and knowledge created).
    /// GET /api/sessions/{sessionId}/graph/summary
    /// </summary>
    [HttpGet("graph/summary")]
    [AllowAnonymous]
    public async Task<IActionResult> GetGraphSummaryAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var client = _graphClientFactory.CreateClient(sessionId);
        var summary = await client.GenerateSessionSummaryAsync(ct);
        return Ok(new
        {
            ok = true,
            sessionId,
            summary
        });
    }

    /// <summary>
    /// Gets the full DAG summary (all nodes and relationships).
    /// GET /api/sessions/{sessionId}/graph/dag-summary
    /// </summary>
    [HttpGet("graph/dag-summary")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDagSummaryAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var client = _graphClientFactory.CreateClient(sessionId);
        var dagSummary = await client.GenerateFullDagSummaryAsync(ct);
        return Ok(new
        {
            ok = true,
            sessionId,
            dagSummary
        });
    }
}
