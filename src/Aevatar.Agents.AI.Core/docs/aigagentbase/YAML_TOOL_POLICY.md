# YAML Tool Policy

## 目标

- 固化 YAML → 工具策略 的默认语义（allowlist + dangerous enablement）。
- 提供清晰的 override 点，避免未来改策略时触碰多处实现。
- 统一审计日志，便于追踪策略变更。

## 运行流程（简化）

1) `AgentYamlConfigApplier.ApplyToolsAndSkillsAsync(...)`
2) `yaml.skills` 存在时：`ConfigureAgentSkillsAsync(...)`（默认 roots）
3) `policy = agent.InternalBuildYamlToolPolicy(yaml)`
4) `SetFixedToolAllowlist(policy.Allowlist or null)`
5) `if policy.EnableDangerousTools => AllowDangerousTools = true`

## 默认行为

- `yaml.tools` → baseline allowlist
- `yaml.skills` 存在 → 自动加入完整 skills 工具面
- dangerous tool 名单默认：`python_exec` + `skills_run_python`
- 仅当 allowlist 含危险工具时，才启用 `AllowDangerousTools`

## 扩展点（override）

- `BuildYamlToolPolicy(...)`：一站式返回策略（allowlist + dangerous 开关）
- `BuildToolAllowlistFromYaml(...)`：allowlist 组合逻辑
- `GetYamlSkillToolNames()`：skills 工具全集
- `GetYamlDefaultSkillRoots()`：YAML 开启 skills 时使用的默认 roots
- `GetSkillToolsAutoIncludedFromYaml(...)`：决定自动注入哪些 skills 工具
- `GetYamlDangerousToolNames()`：dangerous 名单
- `ShouldEnableDangerousToolsFromYaml(...)`：dangerous 开关判定
- `LogYamlToolPolicyDecision(...)`：审计日志（默认 Debug）

## 安全语义

- allowlist 只是“基线可见性”，真正执行仍受 `AllowInternalTools` / `AllowDangerousTools` 约束。
- `AgentYamlConfigApplier` 会在必要时自动进入 InitializationScope，避免 `StateProtection` 误触。

## 审计日志

`LogYamlToolPolicyDecision(...)` 默认记录以下稳定字段：

- `allowlist_count` / `allowlist_hash` / `allowlist_sample`
- `dangerous_names_count` / `enable_dangerous`
- `skills_count` / `tools_count`

如果需要更强审计，可在 override 中加入环境、角色、用户来源等上下文。

## 示例：只允许部分 skills 工具自动注入

```csharp
protected override IReadOnlyCollection<string> GetSkillToolsAutoIncludedFromYaml(
    AgentYamlConfig yaml,
    IReadOnlyCollection<string> skillToolNames)
{
    if (yaml?.Skills is not { Count: > 0 })
        return Array.Empty<string>();

    return new[]
    {
        "skills_list",
        "read_skill_document"
    };
}
```


