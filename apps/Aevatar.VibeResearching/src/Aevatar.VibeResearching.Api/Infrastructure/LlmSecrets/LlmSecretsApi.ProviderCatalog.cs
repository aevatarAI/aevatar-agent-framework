using Aevatar.Agents.Core.Secrets;

namespace VibeResearching.Api.Infrastructure;

public static partial class LlmSecretsApi
{
    private static class ProviderCatalog
    {
        public static IReadOnlyList<ProviderTypeItem> BuildProviderTypes(IAevatarUserSecretsStore secrets)
        {
            var counts = BuildInstances(secrets)
                .GroupBy(x => x.ProviderType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            return ProviderProfiles.All
                .OrderBy(x => string.Equals(x.Category, "popular", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(p => new ProviderTypeItem(
                    Id: p.Id,
                    DisplayName: p.DisplayName,
                    Category: p.Category,
                    Description: p.Description,
                    Recommended: p.Recommended,
                    ConfiguredInstancesCount: counts.TryGetValue(p.Id, out var c) ? c : 0))
                .ToList();
        }

        public static IReadOnlyList<ProviderInstanceItem> BuildInstances(IAevatarUserSecretsStore secrets)
        {
            var names = ExtractConfiguredInstanceNames(secrets);
            var list = new List<ProviderInstanceItem>(names.Count);
            foreach (var name in names)
            {
                var resolved = LlmProviderResolver.Resolve(secrets, name);
                list.Add(new ProviderInstanceItem(
                    Name: name,
                    ProviderType: resolved.ProviderType,
                    ProviderDisplayName: resolved.DisplayName,
                    Model: resolved.Model,
                    Endpoint: resolved.Endpoint));
            }

            return list
                .OrderBy(x => x.ProviderDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static HashSet<string> ExtractConfiguredInstanceNames(IAevatarUserSecretsStore secrets)
        {
            var all = secrets.GetAll();
            const string prefix = "LLMProviders:Providers:";
            const string suffix = ":ApiKey";

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in all.Keys)
            {
                if (!k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var mid = k.Substring(prefix.Length, k.Length - prefix.Length - suffix.Length);
                if (string.IsNullOrWhiteSpace(mid))
                    continue;
                set.Add(mid.Trim());
            }
            return set;
        }
    }
}


