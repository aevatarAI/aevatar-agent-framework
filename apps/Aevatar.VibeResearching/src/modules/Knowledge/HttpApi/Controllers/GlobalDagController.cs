using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Global DAG controller for cross-session knowledge graph operations.
/// </summary>
[ApiController]
[Route("api/dag")]
[AllowAnonymous]
public class GlobalDagController : AbpControllerBase
{
    private readonly IDagStore _dagStore;

    public GlobalDagController(IDagStore dagStore)
    {
        _dagStore = dagStore;
    }

    /// <summary>
    /// Gets the global DAG snapshot (cross-session knowledge graph).
    /// GET /api/dag/global
    /// </summary>
    [HttpGet("global")]
    public async Task<IActionResult> GetGlobalDagAsync(CancellationToken ct = default)
    {
        var dag = await _dagStore.GetSnapshotForListAsync("global", ct);
        return Ok(new { ok = true, dag });
    }
}
