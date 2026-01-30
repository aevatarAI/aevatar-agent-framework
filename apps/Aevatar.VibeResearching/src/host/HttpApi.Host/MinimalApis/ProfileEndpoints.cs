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
