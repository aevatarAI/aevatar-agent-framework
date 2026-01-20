namespace Aevatar.Platform.Core.Config;

// ============================================================
//  ModelDefaults
//
//  说明：
//  - 统一 provider/model 解析逻辑，避免 CLI/TUI 重复
// ============================================================
public static class ModelDefaults
{
    public static string ResolveProvider(
        ModelsConfig models,
        string? providerOverride,
        string? modelOverride)
    {
        ArgumentNullException.ThrowIfNull(models);

        var provider = (providerOverride ?? string.Empty).Trim();
        if (provider.Length > 0)
            return provider;

        provider = (models.DefaultProvider ?? string.Empty).Trim();
        if (provider.Length > 0)
            return provider;

        var model = (modelOverride ?? string.Empty).Trim();
        var index = model.IndexOf('/', StringComparison.Ordinal);
        return index > 0 ? model[..index] : string.Empty;
    }

    public static string ResolveModel(ModelsConfig models, string? modelOverride)
    {
        ArgumentNullException.ThrowIfNull(models);

        var model = (modelOverride ?? string.Empty).Trim();
        if (model.Length > 0)
            return model;

        model = (models.DefaultModel ?? string.Empty).Trim();
        if (model.Length > 0)
            return model;

        var provider = (models.DefaultProvider ?? string.Empty).Trim();
        if (provider.Length == 0)
            return string.Empty;

        return models.Providers.TryGetValue(provider, out var entry)
            ? (entry.DefaultModel ?? string.Empty).Trim()
            : string.Empty;
    }
}
