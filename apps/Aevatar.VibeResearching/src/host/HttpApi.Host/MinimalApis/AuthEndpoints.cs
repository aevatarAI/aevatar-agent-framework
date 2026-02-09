using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace Aevatar.VibeResearching.HttpApi.Host.MinimalApis;

public static class AuthEndpoints
{
    // ─────────────────────────────────────────────────────────────────────
    // Shared HTTP clients for OAuth providers
    // ─────────────────────────────────────────────────────────────────────
    private static readonly HttpClient GitHubHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            Accept = { new MediaTypeWithQualityHeaderValue("application/json") },
            UserAgent = { new ProductInfoHeaderValue("AevatarVibeResearching", "1.0") }
        }
    };

    private static readonly HttpClient GoogleHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            Accept = { new MediaTypeWithQualityHeaderValue("application/json") }
        }
    };

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

        // ─────────────────────────────────────────────────────────────────────
        // POST /api/auth/github/callback — Exchange GitHub code for user session
        // Frontend sends the authorization code; backend exchanges it for a
        // GitHub access token, fetches the user profile, creates or finds
        // the ABP Identity user, signs in, and returns user info.
        // ─────────────────────────────────────────────────────────────────────
        app.MapPost("/api/auth/github/callback", async (
            GitHubCallbackRequest request,
            IConfiguration configuration,
            UserManager<Volo.Abp.Identity.IdentityUser> userManager,
            SignInManager<Volo.Abp.Identity.IdentityUser> signInManager,
            IdentityRoleManager roleManager,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AuthEndpoints.GitHubCallback");

            if (string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { error = "Authorization code is required." });

            // C1: Reject if state is missing — frontend must validate state before calling
            if (string.IsNullOrWhiteSpace(request.State))
                return Results.BadRequest(new { error = "OAuth state parameter is required." });

            var clientId = configuration["GitHub:ClientId"];
            var clientSecret = configuration["GitHub:ClientSecret"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                return Results.Json(new { error = "GitHub OAuth is not configured." }, statusCode: 500);

            // 1. Exchange code → access token
            var tokenPayload = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = request.Code,
            };

            var tokenResp = await GitHubHttp.PostAsync(
                "https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(tokenPayload));

            if (!tokenResp.IsSuccessStatusCode)
                return Results.Json(new { error = "Authentication failed. Please try again." }, statusCode: 502);

            var tokenData = await tokenResp.Content.ReadFromJsonAsync<GitHubTokenResponse>();
            if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                // [C3] Log specific error server-side, return generic message to client
                logger.LogWarning("GitHub token exchange failed: {Error} - {Description}",
                    tokenData?.Error, tokenData?.ErrorDescription);
                return Results.Json(new { error = "Authentication failed. Please try again." }, statusCode: 502);
            }

            // 2. Fetch GitHub user profile
            using var userReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

            var userResp = await GitHubHttp.SendAsync(userReq);
            if (!userResp.IsSuccessStatusCode)
                return Results.Json(new { error = "Authentication failed. Please try again." }, statusCode: 502);

            var ghUser = await userResp.Content.ReadFromJsonAsync<GitHubUserInfo>();
            if (ghUser is null || ghUser.Id == 0)
                return Results.Json(new { error = "Authentication failed. Please try again." }, statusCode: 502);

            // 3. Fetch primary verified email — require verified email (H4)
            var email = ghUser.Email;
            bool emailFromApi = !string.IsNullOrEmpty(email);

            // Always check /user/emails for verified status
            {
                using var emailReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
                emailReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

                var emailResp = await GitHubHttp.SendAsync(emailReq);
                if (emailResp.IsSuccessStatusCode)
                {
                    var emails = await emailResp.Content.ReadFromJsonAsync<List<GitHubEmail>>();
                    var verifiedPrimary = emails?.FirstOrDefault(e => e.Primary && e.Verified)?.Email;
                    var verifiedAny = emails?.FirstOrDefault(e => e.Verified)?.Email;
                    email = verifiedPrimary ?? verifiedAny ?? email;
                }
            }

            if (string.IsNullOrEmpty(email))
            {
                return Results.Json(new
                {
                    error = "A verified email address is required. Please add a verified email to your GitHub account."
                }, statusCode: 400);
            }

            // 4. Find or create ABP Identity user
            var user = await userManager.FindByLoginAsync("GitHub", ghUser.Id.ToString());

            if (user is null)
            {
                // [C2] Check if email already belongs to an existing account
                var existingByEmail = await userManager.FindByEmailAsync(email);

                if (existingByEmail is not null)
                {
                    // Do NOT auto-link — require explicit account linking
                    // Instead, inform user they need to log in with their existing account
                    logger.LogInformation(
                        "GitHub OAuth: email {Email} matches existing user {UserId}, refusing auto-link",
                        email, existingByEmail.Id);
                    return Results.Json(new
                    {
                        error = "An account with this email already exists. Please sign in with your password first, then link your GitHub account from settings."
                    }, statusCode: 409);
                }

                // Create new user
                var userName = ghUser.Login;

                // Ensure username is unique
                var existing = await userManager.FindByNameAsync(userName);
                if (existing is not null)
                    userName = $"{userName}_{ghUser.Id}";

                user = new Volo.Abp.Identity.IdentityUser(
                    Guid.NewGuid(),
                    userName,
                    email);

                user.SetIsActive(true);

                // Set display name
                var displayName = ghUser.Name ?? ghUser.Login;
                var nameParts = displayName.Split(' ', 2);
                user.Name = nameParts[0];
                if (nameParts.Length > 1) user.Surname = nameParts[1];

                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    // [M3] Generic error to client, log specifics server-side
                    logger.LogError("Failed to create user for GitHub {Login}: {Errors}",
                        ghUser.Login,
                        string.Join("; ", createResult.Errors.Select(e => e.Description)));
                    return Results.Json(new { error = "Account creation failed. Please try again." }, statusCode: 500);
                }

                // Assign default "member" role
                if (await roleManager.RoleExistsAsync("member"))
                    await userManager.AddToRoleAsync(user, "member");

                // Link GitHub login to user
                await userManager.AddLoginAsync(user, new UserLoginInfo("GitHub", ghUser.Id.ToString(), "GitHub"));
            }

            // 5. Sign in (creates Identity cookie)
            await signInManager.SignInAsync(user, isPersistent: false);

            // 6. Return ABP Identity user info (not GitHub IDs)
            var roles = await userManager.GetRolesAsync(user);
            var isAdmin = roles.Contains("admin", StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new
            {
                user = new
                {
                    id = user.Id.ToString(),
                    userName = user.UserName,
                    email = user.Email,
                    name = user.Name,
                    surname = user.Surname,
                    roles = roles.ToList(),
                    isAdmin,
                    avatarUrl = ghUser.AvatarUrl,
                    loginProvider = "GitHub",
                }
            });
        }).AllowAnonymous();

        // ─────────────────────────────────────────────────────────────────────
        // POST /api/auth/google/callback — Exchange Google code for user session
        // Frontend sends the authorization code; backend exchanges it for a
        // Google access token, fetches the user profile, creates or finds
        // the ABP Identity user, signs in, and returns user info.
        // ─────────────────────────────────────────────────────────────────────
        app.MapPost("/api/auth/google/callback", async (
            GoogleCallbackRequest request,
            IConfiguration configuration,
            UserManager<Volo.Abp.Identity.IdentityUser> userManager,
            SignInManager<Volo.Abp.Identity.IdentityUser> signInManager,
            IdentityRoleManager roleManager,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AuthEndpoints.GoogleCallback");

            if (string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { error = "Authorization code is required." });

            if (string.IsNullOrWhiteSpace(request.State))
                return Results.BadRequest(new { error = "OAuth state parameter is required." });

            var clientId = configuration["Google:ClientId"];
            var clientSecret = configuration["Google:ClientSecret"];
            var redirectUri = configuration["Google:RedirectUri"] ?? "http://localhost:5173/auth/callback/google";

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                return Results.Json(new { error = "Google OAuth is not configured." }, statusCode: 500);

            // 1. Exchange code → access token
            var tokenPayload = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = request.Code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
            };

            var tokenResp = await GoogleHttp.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(tokenPayload));

            if (!tokenResp.IsSuccessStatusCode)
            {
                var errBody = await tokenResp.Content.ReadAsStringAsync();
                logger.LogWarning("Google token exchange failed: {Status} - {Body}",
                    tokenResp.StatusCode, errBody);
                return Results.Json(new { error = "Authentication failed. Please try again." }, statusCode: 502);
            }

            var tokenData = await tokenResp.Content.ReadFromJsonAsync<GoogleTokenResponse>();
            if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                logger.LogWarning("Google token exchange returned empty access_token");
                return Results.Json(new { error = "Authentication failed. Please try again." }, statusCode: 502);
            }

            // 2. Fetch Google user profile
            using var userReq = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

            var userResp = await GoogleHttp.SendAsync(userReq);
            if (!userResp.IsSuccessStatusCode)
            {
                logger.LogWarning("Google userinfo fetch failed: {Status}", userResp.StatusCode);
                return Results.Json(new { error = "Authentication failed. Please try again." }, statusCode: 502);
            }

            var googleUser = await userResp.Content.ReadFromJsonAsync<GoogleUserInfo>();
            if (googleUser is null || string.IsNullOrEmpty(googleUser.Id))
                return Results.Json(new { error = "Authentication failed. Please try again." }, statusCode: 502);

            // 3. Require verified email
            if (string.IsNullOrEmpty(googleUser.Email))
            {
                return Results.Json(new
                {
                    error = "A verified email address is required. Please use a Google account with a verified email."
                }, statusCode: 400);
            }

            if (!googleUser.VerifiedEmail)
            {
                return Results.Json(new
                {
                    error = "Your Google email is not verified. Please verify your email in your Google account."
                }, statusCode: 400);
            }

            // 4. Find or create ABP Identity user
            var user = await userManager.FindByLoginAsync("Google", googleUser.Id);

            if (user is null)
            {
                // Check if email already belongs to an existing account
                var existingByEmail = await userManager.FindByEmailAsync(googleUser.Email);

                if (existingByEmail is not null)
                {
                    logger.LogInformation(
                        "Google OAuth: email {Email} matches existing user {UserId}, refusing auto-link",
                        googleUser.Email, existingByEmail.Id);
                    return Results.Json(new
                    {
                        error = "An account with this email already exists. Please sign in with your password first, then link your Google account from settings."
                    }, statusCode: 409);
                }

                // Create new user - use email prefix as username
                var userName = googleUser.Email.Split('@')[0];

                // Ensure username is unique
                var existing = await userManager.FindByNameAsync(userName);
                if (existing is not null)
                    userName = $"{userName}_{googleUser.Id[..8]}";

                user = new Volo.Abp.Identity.IdentityUser(
                    Guid.NewGuid(),
                    userName,
                    googleUser.Email);

                user.SetIsActive(true);

                // Set display name
                if (!string.IsNullOrEmpty(googleUser.GivenName))
                    user.Name = googleUser.GivenName;
                if (!string.IsNullOrEmpty(googleUser.FamilyName))
                    user.Surname = googleUser.FamilyName;

                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    logger.LogError("Failed to create user for Google {Email}: {Errors}",
                        googleUser.Email,
                        string.Join("; ", createResult.Errors.Select(e => e.Description)));
                    return Results.Json(new { error = "Account creation failed. Please try again." }, statusCode: 500);
                }

                // Assign default "member" role
                if (await roleManager.RoleExistsAsync("member"))
                    await userManager.AddToRoleAsync(user, "member");

                // Link Google login to user
                await userManager.AddLoginAsync(user, new UserLoginInfo("Google", googleUser.Id, "Google"));
            }

            // 5. Sign in (creates Identity cookie)
            await signInManager.SignInAsync(user, isPersistent: false);

            // 6. Return ABP Identity user info
            var roles = await userManager.GetRolesAsync(user);
            var isAdmin = roles.Contains("admin", StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new
            {
                user = new
                {
                    id = user.Id.ToString(),
                    userName = user.UserName,
                    email = user.Email,
                    name = user.Name,
                    surname = user.Surname,
                    roles = roles.ToList(),
                    isAdmin,
                    avatarUrl = googleUser.Picture,
                    loginProvider = "Google",
                }
            });
        }).AllowAnonymous();
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

/// <summary>
/// Request model for POST /api/auth/github/callback
/// </summary>
public record GitHubCallbackRequest(string Code, string? State = null);

/// <summary>
/// GitHub token exchange response
/// </summary>
internal sealed class GitHubTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// GitHub user profile from /user API
/// </summary>
internal sealed class GitHubUserInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// GitHub email entry from /user/emails API
/// </summary>
internal sealed class GitHubEmail
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }
}

// ─────────────────────────────────────────────────────────────────────
// Google OAuth Models
// ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Request model for POST /api/auth/google/callback
/// </summary>
public record GoogleCallbackRequest(string Code, string? State = null);

/// <summary>
/// Google token exchange response
/// </summary>
internal sealed class GoogleTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// Google user profile from /oauth2/v2/userinfo API
/// </summary>
internal sealed class GoogleUserInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("verified_email")]
    public bool VerifiedEmail { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("given_name")]
    public string? GivenName { get; set; }

    [JsonPropertyName("family_name")]
    public string? FamilyName { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }
}
