namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming

// ============================================================
//  Agent Skills models (internal)
//
//  中文 + ASCII:
//  - 这些类型原先是 AIGAgentBase 的 private nested records，导致无法被独立 runtime 组件复用
//  - 迁移为 internal，以支持 AgentSkillsRuntime 抽离，同时不暴露为 public API
// ============================================================

internal sealed record AgentSkillDescriptor(
    string FolderName,
    string Name,
    string Description,
    IReadOnlyList<string> AllowedTools,
    string DirectoryPath,
    string SkillFilePath,
    IReadOnlyList<string> DotNetToolFiles);

internal sealed record AgentSkillFrontMatter(
    string Name,
    string Description,
    IReadOnlyList<string> AllowedTools);

internal sealed record SkillMarkdown(
    AgentSkillFrontMatter? FrontMatter,
    string Body);

internal enum YamlMode
{
    None,
    Block,
    List
}


