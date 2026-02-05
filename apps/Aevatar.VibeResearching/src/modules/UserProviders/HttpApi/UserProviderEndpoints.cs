using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Aevatar.VibeResearching.UserProviders.HttpApi;

/// <summary>
/// Minimal API endpoints for user LLM provider management.
/// All endpoints require authentication and operate on the current user's data only.
/// </summary>
public static class UserProviderEndpoints
{
    public static void MapUserProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/user/llm/providers")
            .RequireAuthorization()
            .WithTags("User LLM Providers");

        // GET /api/user/llm/providers - List all providers for the current user
        group.MapGet("/", async (
            IUserProviderAppService service,
            CancellationToken ct) =>
        {
            var providers = await service.GetListAsync(ct);
            return Results.Ok(new { providers });
        });

        // POST /api/user/llm/providers - Create a new provider
        group.MapPost("/", async (
            CreateUserProviderDto input,
            IUserProviderAppService service,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.CreateAsync(input, ct);
                return Results.Created($"/api/user/llm/providers/{result.Id}", result);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
            {
                return Results.Conflict(new { error = ex.Message, code = "PROVIDER_NAME_CONFLICT" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("limit"))
            {
                return Results.UnprocessableEntity(new { error = ex.Message, code = "PROVIDER_LIMIT_REACHED" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message, code = "VALIDATION_FAILED" });
            }
        });

        // PUT /api/user/llm/providers/{id} - Update an existing provider
        group.MapPut("/{id}", async (
            string id,
            UpdateUserProviderDto input,
            IUserProviderAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.UpdateAsync(id, input, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Provider not found.", code = "PROVIDER_NOT_FOUND" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
            {
                return Results.Conflict(new { error = ex.Message, code = "PROVIDER_NAME_CONFLICT" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message, code = "VALIDATION_FAILED" });
            }
        });

        // DELETE /api/user/llm/providers/{id} - Delete a provider
        group.MapDelete("/{id}", async (
            string id,
            IUserProviderAppService service,
            CancellationToken ct) =>
        {
            try
            {
                await service.DeleteAsync(id, ct);
                return Results.Ok(new { ok = true });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Provider not found.", code = "PROVIDER_NOT_FOUND" });
            }
        });

        // POST /api/user/llm/providers/{id}/test - Test provider connectivity
        group.MapPost("/{id}/test", async (
            string id,
            IUserProviderAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.TestAsync(id, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Provider not found.", code = "PROVIDER_NOT_FOUND" });
            }
        });

        // GET /api/user/llm/providers/{id}/models - List available models
        group.MapGet("/{id}/models", async (
            string id,
            int? limit,
            IUserProviderAppService service,
            CancellationToken ct) =>
        {
            try
            {
                var models = await service.GetModelsAsync(id, limit ?? 50, ct);
                return Results.Ok(new { models });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Provider not found.", code = "PROVIDER_NOT_FOUND" });
            }
        });

        // PUT /api/user/llm/providers/default - Set default provider
        group.MapPut("/default", async (
            SetDefaultProviderDto input,
            IUserProviderAppService service,
            CancellationToken ct) =>
        {
            try
            {
                await service.SetDefaultAsync(input, ct);
                return Results.Ok(new { ok = true });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Provider not found.", code = "PROVIDER_NOT_FOUND" });
            }
        });
    }
}
