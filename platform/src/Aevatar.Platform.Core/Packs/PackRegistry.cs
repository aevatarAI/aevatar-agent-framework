using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Platform.Core.Packs;

// ============================================================
//  PackRegistry (data-first, local-only)
//
//  Purpose:
//  - Discover Packs (domain bundles) from ~/.aevatar/packs/*
//  - Provide built-in fallback pack(s) so Platform can run without any pack files.
//
//  Pack structure (proposed, minimal):
//  ~/.aevatar/
//    packs/<packName>/
//      pack.yaml            # manifest
//      workflows/*.json|yaml
//      agents/*.yaml
//
//  Notes:
//  - This is local-only; do not persist secrets in pack.yaml.
//  - Full CLI/TUI integration lands in later tasks.
// ============================================================
public sealed class PackRegistry
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private readonly Dictionary<string, PackDefinition> _packs = new(StringComparer.OrdinalIgnoreCase);

    public PackRegistry(string configDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDir);
        ConfigDirectory = configDir;

        LoadBuiltIns();
        LoadUserPacksBestEffort();
    }

    public string ConfigDirectory { get; }

    public IReadOnlyCollection<PackDefinition> GetAllPacks()
        => _packs.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

    public bool TryGetPack(string name, out PackDefinition? pack)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            pack = null;
            return false;
        }

        return _packs.TryGetValue(name, out pack);
    }

    public IReadOnlyCollection<string> GetKnownProfiles()
        => _packs.Values
            .SelectMany(p => p.Profiles.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

    private void LoadBuiltIns()
    {
        // Built-in pack: "core"
        // Keep it tiny: enough to enable profile switching even before any files exist.
        var core = new PackDefinition(
            Name: "core",
            RootDirectory: null,
            Profiles: new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["coding"] = new ProfileDefinition(
                    Profile: "coding",
                    DefaultWorkflow: "agent_router",
                    DefaultRoles: new[] { "hermes", "coder", "reviewer", "tester", "debugger", "search", "docs", "judge" },
                    ToolPolicyPreset: "coding_default"),
                ["vibe"] = new ProfileDefinition(
                    Profile: "vibe",
                    DefaultWorkflow: "vibe",
                    DefaultRoles: new[] { "research_assistant", "planner", "reasoner", "librarian", "verifier", "dag_builder", "paper_editor" },
                    ToolPolicyPreset: "vibe_default"),
                ["worldbuilding"] = new ProfileDefinition(
                    Profile: "worldbuilding",
                    DefaultWorkflow: "worldbuilding",
                    DefaultRoles: new[] { "worldbuilder", "lore_keeper", "character_designer", "plot_architect", "critic" },
                    ToolPolicyPreset: "writing_default")
            });

        _packs[core.Name] = core;
    }

    private void LoadUserPacksBestEffort()
    {
        try
        {
            var packsDir = Path.Combine(ConfigDirectory, "packs");
            if (!Directory.Exists(packsDir))
                return;

            foreach (var dir in Directory.GetDirectories(packsDir))
            {
                var packName = Path.GetFileName(dir).Trim();
                if (packName.Length == 0)
                    continue;

                var manifestPath = Path.Combine(dir, "pack.yaml");
                if (!File.Exists(manifestPath))
                    continue;

                var raw = File.ReadAllText(manifestPath);
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var dto = Yaml.Deserialize<PackManifestDto>(raw);
                var pack = MapPack(dir, packName, dto);
                _packs[pack.Name] = pack; // user packs override built-in by name (last-wins)
            }
        }
        catch
        {
            // best-effort only
        }
    }

    private static PackDefinition MapPack(string rootDir, string fallbackName, PackManifestDto? dto)
    {
        var name = (dto?.Name ?? fallbackName).Trim();
        if (name.Length == 0)
            name = fallbackName;

        var profiles = new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        if (dto?.Profiles != null)
        {
            foreach (var (profileName, p) in dto.Profiles)
            {
                var prof = (profileName ?? string.Empty).Trim();
                if (prof.Length == 0) continue;

                profiles[prof] = new ProfileDefinition(
                    Profile: prof,
                    DefaultWorkflow: (p?.DefaultWorkflow ?? string.Empty).Trim(),
                    DefaultRoles: (p?.DefaultRoles ?? new List<string>())
                        .Select(x => (x ?? string.Empty).Trim())
                        .Where(x => x.Length > 0)
                        .ToArray(),
                    ToolPolicyPreset: (p?.ToolPolicyPreset ?? string.Empty).Trim());
            }
        }

        return new PackDefinition(name, rootDir, profiles);
    }

    // ------------------------------------------------------------
    // DTOs
    // ------------------------------------------------------------
    private sealed class PackManifestDto
    {
        public string? Name { get; set; }

        public Dictionary<string, PackProfileDto>? Profiles { get; set; }
    }

    private sealed class PackProfileDto
    {
        public string? DefaultWorkflow { get; set; }

        public List<string>? DefaultRoles { get; set; }

        public string? ToolPolicyPreset { get; set; }
    }
}

public sealed record PackDefinition(
    string Name,
    string? RootDirectory,
    IReadOnlyDictionary<string, ProfileDefinition> Profiles);

public sealed record ProfileDefinition(
    string Profile,
    string DefaultWorkflow,
    IReadOnlyList<string> DefaultRoles,
    string ToolPolicyPreset);


