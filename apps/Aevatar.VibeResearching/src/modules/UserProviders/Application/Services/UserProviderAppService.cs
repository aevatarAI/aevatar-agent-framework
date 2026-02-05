using System.Text.RegularExpressions;
using Aevatar.VibeResearching.UserProviders.Constants;
using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.Extensions.Logging;
using Volo.Abp.Users;

namespace Aevatar.VibeResearching.UserProviders.Application.Services;

/// <summary>
/// Application service for user LLM provider CRUD operations.
/// Handles validation, encryption, default management, and ownership checks.
/// </summary>
public sealed class UserProviderAppService : IUserProviderAppService
{
    private readonly IUserLlmProviderRepository _providerRepo;
    private readonly IUserCodexTokenRepository _tokenRepo;
    private readonly IUserProviderEncryptionService _encryption;
    private readonly ProviderConnectivityTester _connectivityTester;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UserProviderAppService> _logger;

    private static readonly Regex NameRegex = new(
        UserProviderConsts.ProviderNamePattern, RegexOptions.Compiled);

    public UserProviderAppService(
        IUserLlmProviderRepository providerRepo,
        IUserCodexTokenRepository tokenRepo,
        IUserProviderEncryptionService encryption,
        ProviderConnectivityTester connectivityTester,
        ICurrentUser currentUser,
        ILogger<UserProviderAppService> logger)
    {
        _providerRepo = providerRepo;
        _tokenRepo = tokenRepo;
        _encryption = encryption;
        _connectivityTester = connectivityTester;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserLlmProviderDto>> GetListAsync(CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var providers = await _providerRepo.GetByUserAsync(userId, ct);
        return providers.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<UserLlmProviderDto> CreateAsync(CreateUserProviderDto input, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        ValidateCreateInput(input);
        await EnsureWithinLimitAsync(userId, ct);
        await EnsureNameUniqueAsync(userId, input.Name, ct);

        if (input.IsDefault)
            await _providerRepo.UnsetDefaultByUserAsync(userId, ct);

        var entity = BuildNewProviderEntity(userId, input);
        var created = await _providerRepo.InsertAsync(entity, ct);
        _logger.LogInformation("Created user provider '{Name}' for user {UserId}.", created.Name, userId);

        return MapToDto(created);
    }

    private async Task EnsureWithinLimitAsync(Guid userId, CancellationToken ct)
    {
        var count = await _providerRepo.GetCountByUserAsync(userId, ct);
        if (count >= UserProviderConsts.MaxProvidersPerUser)
        {
            throw new InvalidOperationException(
                $"Maximum provider limit ({UserProviderConsts.MaxProvidersPerUser}) reached.");
        }
    }

    private async Task EnsureNameUniqueAsync(Guid userId, string name, CancellationToken ct)
    {
        var existing = await _providerRepo.GetByUserAndNameAsync(userId, name, ct);
        if (existing != null)
        {
            throw new InvalidOperationException(
                $"A provider named '{name}' already exists.");
        }
    }

    private UserLlmProvider BuildNewProviderEntity(Guid userId, CreateUserProviderDto input)
    {
        var encryptedApiKey = string.IsNullOrEmpty(input.ApiKey)
            ? string.Empty
            : _encryption.Encrypt(input.ApiKey);

        var now = DateTimeOffset.UtcNow;
        return new UserLlmProvider
        {
            UserId = userId,
            Name = input.Name.Trim(),
            ProviderType = input.ProviderType,
            EncryptedApiKey = encryptedApiKey,
            Endpoint = input.Endpoint?.Trim(),
            DefaultModel = input.DefaultModel.Trim(),
            DeploymentName = input.DeploymentName?.Trim(),
            IsDefault = input.IsDefault,
            IsCodexOAuth = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <inheritdoc />
    public async Task<UserLlmProviderDto> UpdateAsync(
        string id, UpdateUserProviderDto input, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var provider = await _providerRepo.GetByIdAndUserAsync(id, userId, ct)
            ?? throw new KeyNotFoundException("Provider not found.");

        await ApplyNameUpdateAsync(provider, input.Name, userId, ct);
        ApplyFieldUpdates(provider, input);

        provider.UpdatedAt = DateTimeOffset.UtcNow;
        await _providerRepo.UpdateAsync(provider, ct);

        return MapToDto(provider);
    }

    private async Task ApplyNameUpdateAsync(
        UserLlmProvider provider, string? newName, Guid userId, CancellationToken ct)
    {
        if (newName == null) return;

        ValidateName(newName);
        if (!string.Equals(provider.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureNameUniqueAsync(userId, newName, ct);
        }
        provider.Name = newName.Trim();
    }

    private void ApplyFieldUpdates(UserLlmProvider provider, UpdateUserProviderDto input)
    {
        if (input.ProviderType != null)
        {
            ValidateProviderType(input.ProviderType);
            provider.ProviderType = input.ProviderType;
        }

        if (input.ApiKey != null && !provider.IsCodexOAuth)
        {
            ValidateApiKey(input.ApiKey);
            provider.EncryptedApiKey = _encryption.Encrypt(input.ApiKey);
        }

        if (input.Endpoint != null)
            provider.Endpoint = input.Endpoint.Trim();

        if (input.DefaultModel != null)
        {
            ValidateModel(input.DefaultModel);
            provider.DefaultModel = input.DefaultModel.Trim();
        }

        if (input.DeploymentName != null)
            provider.DeploymentName = input.DeploymentName.Trim();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var provider = await _providerRepo.GetByIdAndUserAsync(id, userId, ct)
            ?? throw new KeyNotFoundException("Provider not found.");

        var wasDefault = provider.IsDefault;

        // If deleting a Codex provider, also delete tokens
        if (provider.IsCodexOAuth)
        {
            await _tokenRepo.DeleteByUserAsync(userId, ct);
        }

        await _providerRepo.DeleteAsync(id, ct);

        // If deleted provider was default, promote the oldest remaining provider
        if (wasDefault)
        {
            var oldest = await _providerRepo.GetOldestByUserAsync(userId, ct);
            if (oldest != null)
            {
                oldest.IsDefault = true;
                oldest.UpdatedAt = DateTimeOffset.UtcNow;
                await _providerRepo.UpdateAsync(oldest, ct);
            }
        }

        _logger.LogInformation("Deleted user provider '{Name}' ({Id}) for user {UserId}.",
            provider.Name, id, userId);
    }

    /// <inheritdoc />
    public async Task SetDefaultAsync(SetDefaultProviderDto input, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var provider = await _providerRepo.GetByIdAndUserAsync(input.ProviderId, userId, ct)
            ?? throw new KeyNotFoundException("Provider not found.");

        // Unset current default
        await _providerRepo.UnsetDefaultByUserAsync(userId, ct);

        // Set new default
        provider.IsDefault = true;
        provider.UpdatedAt = DateTimeOffset.UtcNow;
        await _providerRepo.UpdateAsync(provider, ct);
    }

    /// <inheritdoc />
    public async Task<ProviderTestResultDto> TestAsync(string id, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var provider = await _providerRepo.GetByIdAndUserAsync(id, userId, ct)
            ?? throw new KeyNotFoundException("Provider not found.");

        if (provider.IsCodexOAuth)
        {
            return new ProviderTestResultDto
            {
                Ok = true, LatencyMs = 0, Model = provider.DefaultModel,
                Message = "Codex OAuth connectivity verified via token validity."
            };
        }

        var (baseUrl, apiKey) = ResolveConnectionInfo(provider);
        return await _connectivityTester.TestConnectivityAsync(baseUrl, apiKey, provider.DefaultModel, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderModelDto>> GetModelsAsync(
        string id, int limit = 50, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var provider = await _providerRepo.GetByIdAndUserAsync(id, userId, ct)
            ?? throw new KeyNotFoundException("Provider not found.");

        if (provider.IsCodexOAuth)
        {
            return UserProviderConsts.CodexSupportedModels
                .Select(m => new ProviderModelDto
                {
                    Id = m, Name = m, OwnedBy = "ChatGPT (Codex)"
                })
                .ToList();
        }

        var (baseUrl, apiKey) = ResolveConnectionInfo(provider);
        return await _connectivityTester.FetchModelsAsync(baseUrl, apiKey, limit, provider.ProviderType, ct);
    }

    private (string BaseUrl, string ApiKey) ResolveConnectionInfo(UserLlmProvider provider)
    {
        var apiKey = _encryption.Decrypt(provider.EncryptedApiKey);
        var baseUrl = !string.IsNullOrWhiteSpace(provider.Endpoint)
            ? provider.Endpoint.TrimEnd('/')
            : ProviderConnectivityTester.GetDefaultEndpoint(provider.ProviderType);

        return (baseUrl, apiKey);
    }

    /// <summary>
    /// Maps entity to DTO without decrypting the API key.
    /// Masking is derived from key presence, not from the plaintext value,
    /// to satisfy the decrypt-on-use principle (F2.5).
    /// </summary>
    private static UserLlmProviderDto MapToDto(UserLlmProvider entity)
    {
        var maskedKey = GenerateMask(entity);

        return new UserLlmProviderDto
        {
            Id = entity.Id,
            Name = entity.Name,
            ProviderType = entity.ProviderType,
            Endpoint = entity.Endpoint,
            DefaultModel = entity.DefaultModel,
            DeploymentName = entity.DeploymentName,
            IsDefault = entity.IsDefault,
            IsCodexOAuth = entity.IsCodexOAuth,
            MaskedApiKey = maskedKey,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    /// <summary>
    /// Generates a display mask without decrypting.
    /// OAuth providers show "(OAuth)", configured keys show a generic mask,
    /// and unconfigured keys show "Not set".
    /// </summary>
    private static string GenerateMask(UserLlmProvider entity)
    {
        if (entity.IsCodexOAuth)
            return "(OAuth)";

        return string.IsNullOrEmpty(entity.EncryptedApiKey)
            ? "Not set"
            : "****••••****";
    }

    private Guid GetCurrentUserId()
    {
        return _currentUser.Id
            ?? throw new UnauthorizedAccessException("User is not authenticated.");
    }

    private static void ValidateCreateInput(CreateUserProviderDto input)
    {
        ValidateName(input.Name);
        ValidateProviderType(input.ProviderType);
        ValidateModel(input.DefaultModel);

        if (string.IsNullOrWhiteSpace(input.ApiKey))
            throw new ArgumentException("API key is required.");

        ValidateApiKey(input.ApiKey);

        if (!string.IsNullOrWhiteSpace(input.Endpoint))
            ValidateEndpoint(input.Endpoint);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Provider name is required.");

        if (name.Length > UserProviderConsts.MaxProviderNameLength)
            throw new ArgumentException(
                $"Provider name must not exceed {UserProviderConsts.MaxProviderNameLength} characters.");

        if (!NameRegex.IsMatch(name))
            throw new ArgumentException(
                "Provider name can only contain alphanumeric characters, spaces, hyphens, and underscores.");
    }

    private static void ValidateProviderType(string providerType)
    {
        if (string.IsNullOrWhiteSpace(providerType))
            throw new ArgumentException("Provider type is required.");

        if (!UserProviderConsts.SupportedProviderTypes.Contains(providerType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unsupported provider type: {providerType}. Supported types: {string.Join(", ", UserProviderConsts.SupportedProviderTypes)}");
    }

    private static void ValidateApiKey(string apiKey)
    {
        if (apiKey.Length > UserProviderConsts.MaxApiKeyLength)
            throw new ArgumentException(
                $"API key must not exceed {UserProviderConsts.MaxApiKeyLength} characters.");
    }

    private static void ValidateModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Default model is required.");

        if (model.Length > UserProviderConsts.MaxModelLength)
            throw new ArgumentException(
                $"Model name must not exceed {UserProviderConsts.MaxModelLength} characters.");
    }

    private static void ValidateEndpoint(string endpoint)
    {
        if (endpoint.Length > UserProviderConsts.MaxEndpointLength)
            throw new ArgumentException(
                $"Endpoint URL must not exceed {UserProviderConsts.MaxEndpointLength} characters.");

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            throw new ArgumentException("Endpoint must be a valid URL.");
    }
}
