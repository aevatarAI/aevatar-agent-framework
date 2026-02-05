using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aevatar.VibeResearching.UserProviders.HttpApi;

/// <summary>
/// Minimal API endpoints for session-level agent-provider mapping.
/// All endpoints require authentication.
/// </summary>
public static class SessionProviderEndpoints
{
    public static void MapSessionProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/sessions/{id}/agent-providers
        endpoints.MapGet("/api/sessions/{id}/agent-providers", async (
            string id,
            ISessionProviderAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetAgentProvidersAsync(id, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Session not found.", code = "SESSION_NOT_FOUND" });
            }
        }).RequireAuthorization().WithTags("Session Providers");

        // PUT /api/sessions/{id}/agent-providers
        endpoints.MapPut("/api/sessions/{id}/agent-providers", async (
            string id,
            UpdateAgentProvidersDto input,
            ISessionProviderAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.UpdateAgentProvidersAsync(id, input, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message, code = "VALIDATION_FAILED" });
            }
        }).RequireAuthorization().WithTags("Session Providers");

        // GET /api/sessions/{id}/available-providers
        endpoints.MapGet("/api/sessions/{id}/available-providers", async (
            string id,
            ISessionProviderAppService service,
            CancellationToken ct) =>
        {
            var providers = await service.GetAvailableProvidersAsync(ct);
            return Results.Ok(new { providers });
        }).RequireAuthorization().WithTags("Session Providers");

        // GET /api/user/llm/available-providers — sessionless, for pre-session provider selection
        endpoints.MapGet("/api/user/llm/available-providers", async (
            ISessionProviderAppService service,
            CancellationToken ct) =>
        {
            var providers = await service.GetAvailableProvidersAsync(ct);
            return Results.Ok(new { providers });
        }).RequireAuthorization().WithTags("Session Providers");
    }
}
