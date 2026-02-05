using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aevatar.VibeResearching.UserProviders.HttpApi;

/// <summary>
/// Minimal API endpoints for Codex OAuth management.
/// All endpoints require authentication.
/// </summary>
public static class CodexOAuthEndpoints
{
    public static void MapCodexOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/user/llm/codex")
            .RequireAuthorization()
            .WithTags("Codex OAuth");

        // POST /api/user/llm/codex/initiate - Start Codex OAuth PKCE flow
        group.MapPost("/initiate", async (
            CodexInitiateRequestDto input,
            ICodexOAuthAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.InitiateAsync(input, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message, code = "VALIDATION_FAILED" });
            }
        });

        // POST /api/user/llm/codex/callback - Complete OAuth flow with authorization code
        group.MapPost("/callback", async (
            CodexCallbackDto input,
            ICodexOAuthAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.HandleCallbackAsync(input, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("Invalid") || ex.Message.Contains("expired"))
            {
                return Results.BadRequest(new { error = ex.Message, code = "CODEX_STATE_INVALID" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("exchange failed"))
            {
                return Results.Json(
                    new { error = ex.Message, code = "CODEX_TOKEN_EXCHANGE_FAILED" },
                    statusCode: 502);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message, code = "VALIDATION_FAILED" });
            }
        });

        // GET /api/user/llm/codex/status - Get Codex connection status
        group.MapGet("/status", async (
            ICodexOAuthAppService service,
            CancellationToken ct) =>
        {
            var result = await service.GetStatusAsync(ct);
            return Results.Ok(result);
        });

        // DELETE /api/user/llm/codex - Disconnect Codex
        group.MapDelete("/", async (
            ICodexOAuthAppService service,
            CancellationToken ct) =>
        {
            await service.DisconnectAsync(ct);
            return Results.Ok(new { ok = true });
        });

        // GET /api/user/llm/codex/auth-mode - Return configured auth mode
        group.MapGet("/auth-mode", (ICodexOAuthAppService service) =>
        {
            return Results.Ok(new { authMode = service.GetAuthMode() });
        });

        // --- Device Code Flow ---

        // POST /api/user/llm/codex/device/initiate - Request a device code
        group.MapPost("/device/initiate", async (
            ICodexOAuthAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.InitiateDeviceCodeAsync(ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new { error = ex.Message, code = "DEVICE_CODE_FAILED" },
                    statusCode: 502);
            }
        });

        // POST /api/user/llm/codex/device/poll - Poll for device authorization
        group.MapPost("/device/poll", async (
            DeviceCodePollRequestDto input,
            ICodexOAuthAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.PollDeviceCodeAsync(input, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message, code = "VALIDATION_FAILED" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new { error = ex.Message, code = "DEVICE_CODE_POLL_FAILED" },
                    statusCode: 502);
            }
        });
    }
}
