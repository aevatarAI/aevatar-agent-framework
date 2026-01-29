# AgentSkills

Agent Skills 运行时实现目录，负责技能目录发现、技能文档解析、工具加载与技能检索。
目标是把与技能相关的 IO/解析/限幅逻辑从 `AIGAgentBase` 中剥离出来。

## 主要内容
- `AgentSkillsRuntime*`：扫描技能目录、解析 `SKILL.md`、加载工具、序列化与资源读取。
- `AgentSkillsEmbeddingsIndex`：技能向量索引与检索能力（用于技能搜索/匹配）。

## 使用方式
- 由 `AIGAgentBase` 内部调用，通常无需直接使用。
- 自定义技能时关注 `SKILL.md` 描述与工具文件规范。

