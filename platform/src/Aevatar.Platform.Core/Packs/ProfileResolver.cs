namespace Aevatar.Platform.Core.Packs;

// ============================================================
//  ProfileResolver
//
//  Purpose:
//  - Resolve the active profile deterministically.
//  - Provide a stable mapping to Pack + ProfileDefinition.
//
//  Precedence (hard rule):
//  - CLI flag > session state > config default
// ============================================================
public static class ProfileResolver
{
    public static ProfileResolutionResult Resolve(
        PackRegistry registry,
        string? cliProfile,
        string? sessionProfile,
        string? configDefaultProfile)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var chosen = FirstNonEmpty(cliProfile, sessionProfile, configDefaultProfile, "coding");

        if (TryResolveProfile(registry, chosen, out var resolved))
            return resolved!;

        // Fallback: keep deterministic and user-friendly.
        var fallback = "coding";
        if (TryResolveProfile(registry, fallback, out var resolvedFallback))
        {
            return resolvedFallback! with
            {
                Warnings = new[]
                {
                    $"Unknown profile '{chosen}'. Falling back to '{fallback}'."
                }
            };
        }

        // Should not happen because built-in pack provides coding profile.
        return new ProfileResolutionResult(
            Profile: fallback,
            PackName: "core",
            WorkflowName: "standard",
            Roles: Array.Empty<string>(),
            ToolPolicyPreset: "",
            Warnings: new[] { $"Unknown profile '{chosen}' and fallback '{fallback}' unavailable." });
    }

    private static bool TryResolveProfile(PackRegistry registry, string profile, out ProfileResolutionResult? result)
    {
        result = null;

        var p = (profile ?? string.Empty).Trim();
        if (p.Length == 0)
            return false;

        foreach (var pack in registry.GetAllPacks())
        {
            if (!pack.Profiles.TryGetValue(p, out var def))
                continue;

            var workflow = string.IsNullOrWhiteSpace(def.DefaultWorkflow) ? "standard" : def.DefaultWorkflow;

            result = new ProfileResolutionResult(
                Profile: p,
                PackName: pack.Name,
                WorkflowName: workflow,
                Roles: def.DefaultRoles.ToArray(),
                ToolPolicyPreset: def.ToolPolicyPreset ?? string.Empty,
                Warnings: Array.Empty<string>());
            return true;
        }

        return false;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            var t = (v ?? string.Empty).Trim();
            if (t.Length > 0)
                return t;
        }

        return string.Empty;
    }
}

public sealed record ProfileResolutionResult(
    string Profile,
    string PackName,
    string WorkflowName,
    IReadOnlyList<string> Roles,
    string ToolPolicyPreset,
    IReadOnlyList<string> Warnings);


