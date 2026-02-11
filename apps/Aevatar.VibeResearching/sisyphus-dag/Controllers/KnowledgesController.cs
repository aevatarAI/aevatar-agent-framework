namespace SisyphusDag.Controllers;

using Microsoft.AspNetCore.Mvc;
using SisyphusDag.Dtos;
using SisyphusDag.Services;

/// <summary>
/// HTTP endpoint for knowledge DAG CRUD operations.
/// Contains ONLY request handling and error handling -- no business logic.
/// </summary>
[ApiController]
[Route("api/knowledges")]
public class KnowledgesController : ControllerBase
{
    private readonly IKnowledgeService _service;
    private readonly ILogger<KnowledgesController> _logger;

    public KnowledgesController(IKnowledgeService service, ILogger<KnowledgesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Creates knowledge nodes and their dependency edges.
    /// </summary>
    [HttpPost]
    public Task<IActionResult> Create(
        [FromBody] KnowledgesDto request,
        CancellationToken ct)
    {
        return HandleAsync(async () =>
        {
            ValidateCreateRequest(request);
            var ids = await _service.CreateAsync(request, ct);
            return Ok(ids);
        });
    }

    /// <summary>
    /// Returns a complete snapshot of the knowledge DAG.
    /// </summary>
    [HttpGet]
    public Task<IActionResult> GetSnapshot(CancellationToken ct)
    {
        return HandleAsync(async () =>
        {
            var snapshot = await _service.GetSnapshotAsync(ct);
            return Ok(snapshot);
        });
    }

    /// <summary>
    /// Updates knowledge nodes by ID.
    /// </summary>
    [HttpPut]
    public Task<IActionResult> Update(
        [FromBody] Dictionary<string, KnowledgeDto> updates,
        CancellationToken ct)
    {
        return HandleAsync(async () =>
        {
            ValidateNotNullOrEmpty(updates, "Updates map");
            var ids = await _service.UpdateAsync(updates, ct);
            return Ok(ids);
        });
    }

    /// <summary>
    /// Deletes knowledge nodes (DETACH DELETE) by ID.
    /// </summary>
    [HttpDelete]
    public Task<IActionResult> Delete(
        [FromBody] List<string> ids,
        CancellationToken ct)
    {
        return HandleAsync(async () =>
        {
            ValidateNotNullOrEmpty(ids, "Node ID list");
            var deleted = await _service.DeleteAsync(ids, ct);
            return Ok(deleted);
        });
    }

    /// <summary>
    /// Returns a markdown explanation of a knowledge node including its derivation chain.
    /// </summary>
    [HttpGet("{id}/explain")]
    public Task<IActionResult> Explain(
        string id,
        [FromQuery] int level = 10,
        CancellationToken ct = default)
    {
        return HandleAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Knowledge node ID must not be empty.");

            var markdown = await _service.ExplainAsync(id, level, ct);
            return Content(markdown, "text/markdown");
        });
    }

    /// <summary>
    /// Uniform error handling wrapper for all actions.
    /// Maps ArgumentException to 400, KeyNotFoundException to 404, all others to 500.
    /// </summary>
    private async Task<IActionResult> HandleAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new ErrorResponse { Error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return new NotFoundObjectResult(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in {Action}", action.Method.Name);
            return new ObjectResult(new ErrorResponse { Error = "An internal error occurred." })
            {
                StatusCode = 500,
            };
        }
    }

    private static void ValidateCreateRequest(KnowledgesDto request)
    {
        if (request.KnowledgeList.Count == 0)
            throw new ArgumentException("KnowledgeList must not be empty.");

        for (var i = 0; i < request.KnowledgeList.Count; i++)
        {
            var dto = request.KnowledgeList[i];
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new ArgumentException($"Title is required at index {i}.");
            if (string.IsNullOrWhiteSpace(dto.Description))
                throw new ArgumentException($"Description is required at index {i}.");
            if (string.IsNullOrWhiteSpace(dto.DeriveDetail))
                throw new ArgumentException($"DeriveDetail is required at index {i}.");
        }

        ValidateIndexDependencies(request);
    }

    private static void ValidateIndexDependencies(KnowledgesDto request)
    {
        if (request.IndexDependencies is null) return;

        var count = request.KnowledgeList.Count;
        foreach (var (parentIdx, childIndices) in request.IndexDependencies)
        {
            if (parentIdx < 0 || parentIdx >= count)
                throw new ArgumentException(
                    $"IndexDependencies parent index {parentIdx} is out of bounds [0, {count}).");

            foreach (var childIdx in childIndices)
            {
                if (childIdx < 0 || childIdx >= count)
                    throw new ArgumentException(
                        $"IndexDependencies child index {childIdx} is out of bounds [0, {count}).");
            }
        }
    }

    private static void ValidateNotNullOrEmpty<T>(ICollection<T>? collection, string name)
    {
        if (collection is null || collection.Count == 0)
            throw new ArgumentException($"{name} must not be null or empty.");
    }

    private static void ValidateNotNullOrEmpty<TKey, TValue>(
        IDictionary<TKey, TValue>? dict,
        string name)
    {
        if (dict is null || dict.Count == 0)
            throw new ArgumentException($"{name} must not be null or empty.");
    }
}
