namespace SisyphusMaker.Controllers;

using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SisyphusMaker.Dtos;
using SisyphusMaker.Services;
using SisyphusMaker.Validation;

/// <summary>
/// Single SSE streaming endpoint for consensus verification.
/// </summary>
[ApiController]
[Route("api/verify")]
public class VerificationController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IVerificationService _service;
    private readonly MakerOptions _makerOptions;
    private readonly ILogger<VerificationController> _logger;

    public VerificationController(
        IVerificationService service,
        IOptions<MakerOptions> makerOptions,
        ILogger<VerificationController> logger)
    {
        _service = service;
        _makerOptions = makerOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Verification endpoint. Renders prompt, runs CognitiveStrategy maker
    /// consensus, and streams progress via SSE.
    /// </summary>
    [HttpPost]
    public async Task Verify(
        [FromBody] VerifyRequest request,
        CancellationToken ct)
    {
        var validationError = RequestValidator.Validate(request);
        if (validationError is not null)
        {
            await WriteValidationErrorAsync(validationError, ct);
            return;
        }

        var jobId = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        SetSseHeaders();
        await Response.Body.FlushAsync(ct);

        await using var writer = new StreamWriter(Response.Body, leaveOpen: true);

        try
        {
            var workerCount = request.Config?.WorkerCount ?? _makerOptions.WorkerCount;
            await WriteSseEventAsync(writer, "started",
                new StartedEvent(jobId, "inline", workerCount));

            var result = await _service.VerifyAsync(
                request,
                async sseEvent => await WriteSseEventAsync(
                    writer, sseEvent.EventType, sseEvent.Data),
                ct);

            result.DurationMs = stopwatch.ElapsedMilliseconds;
            await WriteSseEventAsync(writer, "result", result);
        }
        catch (OperationCanceledException)
        {
            await WriteSseEventAsync(writer, "error",
                new ErrorEvent("Request timed out or was cancelled.", jobId,
                    stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during verification {JobId}", jobId);
            await WriteSseEventAsync(writer, "error",
                new ErrorEvent("An internal error occurred during verification.",
                    jobId, stopwatch.ElapsedMilliseconds));
        }
    }

    private void SetSseHeaders()
    {
        Response.StatusCode = 200;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
    }

    private async Task WriteValidationErrorAsync(string error, CancellationToken ct)
    {
        Response.StatusCode = 400;
        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(
            new ErrorResponse { Error = error }, JsonOptions, ct);
    }

    private static async Task WriteSseEventAsync(
        StreamWriter writer,
        string eventType,
        object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await writer.WriteAsync($"event: {eventType}\ndata: {json}\n\n");
        await writer.FlushAsync();
    }
}
