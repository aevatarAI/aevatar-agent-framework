using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.ObjectExtending;
using Volo.Abp.Users;

namespace Aevatar.VibeResearching.HttpApi.Host.MinimalApis;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app)
    {
        app.MapGet("/api/vibe/my-profile", async (
            ICurrentUser currentUser,
            IIdentityUserRepository userRepository) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.Id == null)
                return Results.Unauthorized();

            var user = await userRepository.GetAsync(currentUser.Id.Value);

            return Results.Json(new
            {
                id = user.Id,
                userName = user.UserName,
                email = user.Email,
                name = user.Name ?? "",
                surname = user.Surname ?? "",
                phoneNumber = user.PhoneNumber ?? "",
                displayName = user.GetProperty<string>("DisplayName") ?? "",
                bio = user.GetProperty<string>("Bio") ?? "",
                hasProfilePicture = user.GetProperty<bool?>("HasProfilePicture") ?? false
            });
        }).RequireAuthorization();

        app.MapPut("/api/vibe/my-profile", async (
            ICurrentUser currentUser,
            IIdentityUserRepository userRepository,
            IdentityUserManager userManager,
            HttpRequest request) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.Id == null)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<UpdateProfileRequest>();
            if (body == null)
                return Results.BadRequest(new { error = "Invalid request body" });

            var user = await userRepository.GetAsync(currentUser.Id.Value);

            // Update standard ABP Identity fields
            if (body.Name != null)
                user.Name = body.Name;

            if (body.Surname != null)
                user.Surname = body.Surname;

            if (body.PhoneNumber != null)
                await userManager.SetPhoneNumberAsync(user, body.PhoneNumber);

            // Update custom extension properties
            if (body.DisplayName != null)
                user.SetProperty("DisplayName", body.DisplayName);

            if (body.Bio != null)
                user.SetProperty("Bio", body.Bio);

            await userManager.UpdateAsync(user);

            return Results.Json(new { ok = true });
        }).RequireAuthorization();

        app.MapPost("/api/vibe/change-password", async (
            ICurrentUser currentUser,
            IdentityUserManager userManager,
            IIdentityUserRepository userRepository,
            HttpRequest request) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.Id == null)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<ChangePasswordRequest>();
            if (body == null || string.IsNullOrWhiteSpace(body.CurrentPassword) || string.IsNullOrWhiteSpace(body.NewPassword))
                return Results.BadRequest(new { error = "currentPassword and newPassword are required" });

            var user = await userRepository.GetAsync(currentUser.Id.Value);

            var result = await userManager.ChangePasswordAsync(user, body.CurrentPassword, body.NewPassword);
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(e => e.Description).ToList();
                return Results.BadRequest(new { error = "Password change failed", details = errors });
            }

            return Results.Json(new { ok = true });
        }).RequireAuthorization();

        // ============================================================
        // User Statistics API - Optimized for admin dashboard
        // ============================================================
        app.MapGet("/api/vibe/user-stats", async (
            IIdentityUserRepository userRepository,
            IIdentityRoleRepository roleRepository,
            IdentityUserManager userManager) =>
        {
            // Get all users and roles
            var users = await userRepository.GetListAsync();
            var roles = await roleRepository.GetListAsync();

            // Calculate role statistics
            var roleStats = new List<object>();
            foreach (var role in roles)
            {
                var usersInRole = await userManager.GetUsersInRoleAsync(role.Name);
                roleStats.Add(new
                {
                    roleName = role.Name,
                    userCount = usersInRole.Count
                });
            }

            return Results.Json(new
            {
                totalUsers = users.Count,
                activeUsers = users.Count(u => u.IsActive),
                inactiveUsers = users.Count(u => !u.IsActive),
                totalRoles = roles.Count,
                roleStats
            });
        }).RequireAuthorization();

        // ============================================================
        // Users List API with Role Filter - Server-side filtering
        // ============================================================
        app.MapGet("/api/vibe/users", async (
            IIdentityUserRepository userRepository,
            IdentityUserManager userManager,
            [FromQuery] string? roleName = null,
            [FromQuery] string? status = null,
            [FromQuery] string? filter = null,
            [FromQuery] int skipCount = 0,
            [FromQuery] int maxResultCount = 10) =>
        {
            IEnumerable<IdentityUser> users;

            // Filter by role if specified
            if (!string.IsNullOrEmpty(roleName))
            {
                users = await userManager.GetUsersInRoleAsync(roleName);
            }
            else
            {
                users = await userRepository.GetListAsync();
            }

            // Filter by status
            if (status == "active")
                users = users.Where(u => u.IsActive);
            else if (status == "inactive")
                users = users.Where(u => !u.IsActive);

            // Search filter
            if (!string.IsNullOrEmpty(filter))
            {
                var lowerFilter = filter.ToLowerInvariant();
                users = users.Where(u =>
                    (u.UserName?.ToLowerInvariant().Contains(lowerFilter) ?? false) ||
                    (u.Email?.ToLowerInvariant().Contains(lowerFilter) ?? false) ||
                    (u.Name?.ToLowerInvariant().Contains(lowerFilter) ?? false) ||
                    (u.Surname?.ToLowerInvariant().Contains(lowerFilter) ?? false));
            }

            // Order by creation time descending
            var orderedUsers = users.OrderByDescending(u => u.CreationTime).ToList();
            var totalCount = orderedUsers.Count;

            // Paginate
            var pagedUsers = orderedUsers.Skip(skipCount).Take(maxResultCount).ToList();

            // Get roles for each user in the page
            var items = new List<object>();
            foreach (var user in pagedUsers)
            {
                var userRoles = await userManager.GetRolesAsync(user);
                items.Add(new
                {
                    id = user.Id,
                    userName = user.UserName,
                    email = user.Email,
                    name = user.Name,
                    surname = user.Surname,
                    phoneNumber = user.PhoneNumber,
                    isActive = user.IsActive,
                    lockoutEnabled = user.LockoutEnabled,
                    lockoutEnd = user.LockoutEnd,
                    emailConfirmed = user.EmailConfirmed,
                    creationTime = user.CreationTime,
                    roles = userRoles
                });
            }

            return Results.Json(new
            {
                items,
                totalCount
            });
        }).RequireAuthorization();
    }

    private sealed record UpdateProfileRequest(
        string? Name,
        string? Surname,
        string? PhoneNumber,
        string? DisplayName,
        string? Bio
    );
    private sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
}
