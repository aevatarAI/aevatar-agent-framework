namespace ScientificResearchAssistant.Api.Infrastructure;

/// <summary>
/// Generic Git-based Agent Skills packs (SKILL.md + scripts/references/assets).
/// </summary>
public sealed class SkillPacksOptions
{
    public const string SectionName = "SkillPacks";

    public List<SkillPackSpec> Packs { get; set; } = new();
}

public sealed class SkillPackSpec
{
    /// <summary>
    /// Logical pack name (used for logging + default install dir).
    /// </summary>
    public string Name { get; set; } = "skill-pack";

    public bool Enabled { get; set; } = true;
    public bool AutoUpdateOnStartup { get; set; } = true;

    /// <summary>
    /// Git remote URL (e.g. https://github.com/org/repo.git).
    /// </summary>
    public string RepoUrl { get; set; } = string.Empty;

    /// <summary>
    /// Branch or tag (default: main).
    /// </summary>
    public string Ref { get; set; } = "main";

    /// <summary>
    /// Directory under repo root that contains skill folders.
    /// </summary>
    public string SkillsSubDir { get; set; } = "skills";

    /// <summary>
    /// Local clone directory. If empty, defaults to "{assistantRoot}/.skillpacks/{Name}".
    /// </summary>
    public string? InstallDir { get; set; }

    public int UpdateTimeoutMs { get; set; } = 60_000;

    public bool ShallowClone { get; set; } = true;
    public int ShallowDepth { get; set; } = 1;

    /// <summary>
    /// If true, append skills root path into env var AEVATAR_AGENT_SKILLS_DIRS at runtime.
    /// </summary>
    public bool SetAgentSkillsEnv { get; set; } = true;
}


