using Aevatar.VibeResearching.Infrastructure.Permissions;
using Aevatar.VibeResearching.Sessions.Permissions;
using OpenIddict.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace Aevatar.VibeResearching.HttpApi.Host.Data;

public class AuthDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionManager _permissionManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IConfiguration _configuration;

    private const string MemberRoleName = "member";
    private const string AdminRoleName = "admin";

    public AuthDataSeedContributor(
        IIdentityRoleRepository roleRepository,
        IdentityRoleManager roleManager,
        IPermissionManager permissionManager,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        IConfiguration configuration)
    {
        _roleRepository = roleRepository;
        _roleManager = roleManager;
        _permissionManager = permissionManager;
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _configuration = configuration;
    }

    [UnitOfWork]
    public async Task SeedAsync(DataSeedContext context)
    {
        await SeedRolesAsync();
        await SeedPermissionsAsync();
        await SeedOpenIddictScopesAsync();
        await SeedOpenIddictApplicationAsync();
    }

    private async Task SeedOpenIddictScopesAsync()
    {
        // Register the custom VibeResearching scope
        const string scopeName = "VibeResearching";

        if (await _scopeManager.FindByNameAsync(scopeName) == null)
        {
            await _scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = scopeName,
                DisplayName = "Vibe Researching API",
                Description = "Access to the Vibe Researching API"
            });
        }
    }

    private async Task SeedRolesAsync()
    {
        // Member role — auto-assigned to new users
        var existingMember = await _roleRepository.FindByNormalizedNameAsync(MemberRoleName.ToUpperInvariant());
        if (existingMember == null)
        {
            var memberRole = new IdentityRole(Guid.NewGuid(), MemberRoleName)
            {
                IsDefault = true, // auto-assign to new users
                IsPublic = true,
                IsStatic = true
            };
            await _roleManager.CreateAsync(memberRole);
        }
        else if (!existingMember.IsDefault)
        {
            // Fix: ensure IsDefault is true for existing role
            existingMember.IsDefault = true;
            existingMember.IsPublic = true;
            existingMember.IsStatic = true;
            await _roleManager.UpdateAsync(existingMember);
        }

        // Admin role
        var existingAdmin = await _roleRepository.FindByNormalizedNameAsync(AdminRoleName.ToUpperInvariant());
        if (existingAdmin == null)
        {
            var adminRole = new IdentityRole(Guid.NewGuid(), AdminRoleName)
            {
                IsDefault = false,
                IsPublic = true,
                IsStatic = true
            };
            await _roleManager.CreateAsync(adminRole);
        }
    }

    private async Task SeedPermissionsAsync()
    {
        // Member role permissions
        var memberPermissions = new[]
        {
            SessionsPermissions.Sessions.View,
            SessionsPermissions.Sessions.ListAll,
            SessionsPermissions.Sessions.Create,
            SessionsPermissions.Sessions.Edit,
            SessionsPermissions.Sessions.Pause,
            SessionsPermissions.Sessions.Resume,
            SessionsPermissions.Sessions.Terminate,
            "VibeResearching.Agents.Orchestration.Execute",
            "VibeResearching.Agents.Orchestration.Cancel",
        };

        foreach (var permission in memberPermissions)
        {
            await _permissionManager.SetForRoleAsync(MemberRoleName, permission, true);
        }

        // Admin role permissions (all member permissions plus admin-specific)
        var adminExtraPermissions = new[]
        {
            SessionsPermissions.Sessions.Delete,
            SessionsPermissions.Sessions.Admin,
            "VibeResearching.Agents.ReviewAgent.Trigger",
            PlatformPermissions.Settings.Default,
            PlatformPermissions.Settings.LLM,
            PlatformPermissions.UserManagement.Default,
            PlatformPermissions.UserManagement.ManageRoles,
            PlatformPermissions.UserManagement.ManagePermissions,
        };

        // Grant member permissions to admin too
        foreach (var permission in memberPermissions)
        {
            await _permissionManager.SetForRoleAsync(AdminRoleName, permission, true);
        }

        foreach (var permission in adminExtraPermissions)
        {
            await _permissionManager.SetForRoleAsync(AdminRoleName, permission, true);
        }
    }

    private async Task SeedOpenIddictApplicationAsync()
    {
        const string clientId = "VibeResearching_React";

        var existingClient = await _applicationManager.FindByClientIdAsync(clientId);
        if (existingClient != null)
        {
            return; // Already seeded
        }

        var rootUrl = _configuration["OpenIddict:Applications:VibeResearching_React:RootUrl"]
                      ?? "http://localhost:5173";

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            DisplayName = "Vibe Researching React SPA",
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
        };

        descriptor.Permissions.UnionWith(
        [
            // Endpoints
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.Endpoints.EndSession,
            OpenIddictConstants.Permissions.Endpoints.Revocation,

            // Grant types
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            OpenIddictConstants.Permissions.GrantTypes.RefreshToken,

            // Response types
            OpenIddictConstants.Permissions.ResponseTypes.Code,

            // Scopes
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "scp:offline_access",
            "scp:VibeResearching",
        ]);

        descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

        descriptor.RedirectUris.Add(new Uri($"{rootUrl.TrimEnd('/')}/signin-callback"));
        descriptor.PostLogoutRedirectUris.Add(new Uri($"{rootUrl.TrimEnd('/')}"));

        await _applicationManager.CreateAsync(descriptor);
    }
}
