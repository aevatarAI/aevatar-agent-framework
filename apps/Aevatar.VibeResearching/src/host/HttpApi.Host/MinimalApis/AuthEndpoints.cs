using Microsoft.AspNetCore.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace Aevatar.VibeResearching.HttpApi.Host.MinimalApis;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // ─────────────────────────────────────────────────────────────────────
        // POST /api/auth/login — Authenticate user and create cookie session
        // Used by SPA before triggering OIDC redirect
        // ─────────────────────────────────────────────────────────────────────
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            SignInManager<Volo.Abp.Identity.IdentityUser> signInManager,
            UserManager<Volo.Abp.Identity.IdentityUser> userManager) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserNameOrEmail) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Username and password are required." });
            }

            // Find user by email or username
            var user = await userManager.FindByEmailAsync(request.UserNameOrEmail)
                       ?? await userManager.FindByNameAsync(request.UserNameOrEmail);

            if (user == null)
            {
                return Results.Json(new { error = "Invalid username or password." }, statusCode: 401);
            }

            // Attempt sign-in (creates Identity.Application cookie)
            var result = await signInManager.PasswordSignInAsync(
                user,
                request.Password,
                isPersistent: request.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                return Results.Ok(new { success = true });
            }

            if (result.IsLockedOut)
            {
                return Results.Json(new { error = "Account is locked. Please try again later." }, statusCode: 423);
            }

            if (result.IsNotAllowed)
            {
                return Results.Json(new { error = "Login not allowed. Please verify your email." }, statusCode: 403);
            }

            return Results.Json(new { error = "Invalid username or password." }, statusCode: 401);
        }).AllowAnonymous();

        // ─────────────────────────────────────────────────────────────────────
        // POST /api/auth/logout — Sign out and clear cookie session
        // ─────────────────────────────────────────────────────────────────────
        app.MapPost("/api/auth/logout", async (
            SignInManager<Volo.Abp.Identity.IdentityUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok(new { success = true });
        }).AllowAnonymous();

        // ─────────────────────────────────────────────────────────────────────
        // GET /api/account/my-permissions — Get current user's granted permissions
        // ─────────────────────────────────────────────────────────────────────
        app.MapGet("/api/account/my-permissions", async (
            IPermissionChecker permissionChecker,
            IPermissionDefinitionManager permissionDefinitionManager,
            ICurrentUser currentUser) =>
        {
            if (!currentUser.IsAuthenticated)
                return Results.Unauthorized();

            var permissions = await permissionDefinitionManager.GetPermissionsAsync();
            var names = permissions.Select(p => p.Name).ToArray();

            // Batch check all permissions concurrently instead of serial N calls
            var results = await Task.WhenAll(
                names.Select(name => permissionChecker.IsGrantedAsync(name)));

            var granted = names.Where((_, i) => results[i]).ToList();

            return Results.Json(new { permissions = granted });
        }).RequireAuthorization();
    }
}

/// <summary>
/// Request model for POST /api/auth/login
/// </summary>
public record LoginRequest(
    string UserNameOrEmail,
    string Password,
    bool RememberMe = false
);
