using Aevatar.VibeResearching.HttpApi.Host.Blobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Volo.Abp.ObjectExtending;
using Volo.Abp.Users;

namespace Aevatar.VibeResearching.HttpApi.Host.MinimalApis;

public static class ProfilePictureEndpoints
{
    public static void MapProfilePictureEndpoints(this WebApplication app)
    {
        app.MapPut("/api/account/profile-picture", async (
            ICurrentUser currentUser,
            IBlobContainer<UserProfilePhotoContainer> blobContainer,
            Volo.Abp.Identity.IIdentityUserRepository userRepository,
            Volo.Abp.Identity.IdentityUserManager userManager,
            HttpRequest request) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.Id == null)
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data" });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
                return Results.BadRequest(new { error = "No file provided" });

            if (file.Length > ImageFormatValidator.MaxFileSizeBytes)
                return Results.BadRequest(new { error = "File too large. Maximum size is 2MB." });

            if (!ImageFormatValidator.AllowedContentTypes.Contains(file.ContentType))
                return Results.BadRequest(new { error = "Invalid file type. Allowed: JPG, PNG, WebP." });

            // Validate magic bytes
            using var stream = file.OpenReadStream();
            var header = new byte[12];
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, 12));
            stream.Position = 0;

            if (!ImageFormatValidator.IsValidImageFormat(header, bytesRead))
                return Results.BadRequest(new { error = "File content does not match a supported image format." });

            var blobName = $"{currentUser.Id}";
            await blobContainer.SaveAsync(blobName, stream, overrideExisting: true);

            // Mark user as having a profile picture
            var user = await userRepository.GetAsync(currentUser.Id.Value);
            user.SetProperty("HasProfilePicture", true);
            await userManager.UpdateAsync(user);

            return Results.Json(new
            {
                ok = true,
                url = $"/api/account/profile-picture/{currentUser.Id}"
            });
        }).RequireAuthorization()
          .DisableAntiforgery();

        app.MapGet("/api/account/profile-picture/{userId}", async (
            string userId,
            IBlobContainer<UserProfilePhotoContainer> blobContainer) =>
        {
            var blob = await blobContainer.GetOrNullAsync(userId);
            if (blob == null)
                return Results.NotFound(new { error = "No profile picture found" });

            // Detect actual content type from magic bytes
            var header = new byte[12];
            var bytesRead = await blob.ReadAsync(header.AsMemory(0, 12));
            blob.Position = 0;
            var contentType = ImageFormatValidator.DetectContentType(header, bytesRead);

            return Results.File(blob, contentType: contentType, enableRangeProcessing: true);
        }).AllowAnonymous();

        app.MapDelete("/api/account/profile-picture", async (
            ICurrentUser currentUser,
            IBlobContainer<UserProfilePhotoContainer> blobContainer,
            Volo.Abp.Identity.IIdentityUserRepository userRepository,
            Volo.Abp.Identity.IdentityUserManager userManager) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.Id == null)
                return Results.Unauthorized();

            var blobName = $"{currentUser.Id}";
            if (await blobContainer.ExistsAsync(blobName))
            {
                await blobContainer.DeleteAsync(blobName);
            }

            var user = await userRepository.GetAsync(currentUser.Id.Value);
            user.SetProperty("HasProfilePicture", false);
            await userManager.UpdateAsync(user);

            return Results.Json(new { ok = true });
        }).RequireAuthorization();
    }
}
