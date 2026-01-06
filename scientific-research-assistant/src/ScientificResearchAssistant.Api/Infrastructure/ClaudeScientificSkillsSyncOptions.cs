namespace ScientificResearchAssistant.Api.Infrastructure;

/// <summary>
/// Claude Scientific Skills (GitHub repo) sync options.
/// <para/>
/// Repo: https://github.com/K-Dense-AI/claude-scientific-skills
/// </summary>
public sealed class ClaudeScientificSkillsSyncOptions
{
    public const string SectionName = "ClaudeScientificSkills";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// If true, the backend will try to clone/pull the repo on startup (best-effort).
    /// </summary>
    public bool AutoUpdateOnStartup { get; set; } = true;

    /// <summary>
    /// Git remote URL.
    /// </summary>
    public string RepoUrl { get; set; } = "https://github.com/K-Dense-AI/claude-scientific-skills.git";

    /// <summary>
    /// Branch or tag to track (default: main).
    /// </summary>
    public string Ref { get; set; } = "main";

    /// <summary>
    /// Local clone directory. When empty, defaults to "~/.aevatar/skillpacks/claude-scientific-skills".
    /// </summary>
    public string? InstallDir { get; set; }

    public int UpdateTimeoutMs { get; set; } = 60_000;

    public bool ShallowClone { get; set; } = true;
    public int ShallowDepth { get; set; } = 1;

    /// <summary>
    /// The directory under repo root that contains skill folders.
    /// In K-Dense repo this is "scientific-skills".
    /// </summary>
    public string SkillsSubDir { get; set; } = "scientific-skills";

    /// <summary>
    /// If true, after sync we set/append AEVATAR_AGENT_SKILLS_DIRS to include the local skill root path.
    /// </summary>
    public bool SetAgentSkillsEnv { get; set; } = true;
}


