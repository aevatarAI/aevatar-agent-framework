## materials/（NotebookLM 风格 sources）

这个目录的本质是：**可引用的“资料库”**。系统会递归扫描其中的 `.md/.txt` 文件，构建 bounded 的 “Materials context”，用于 vibe researching 的 grounding。

关键点：

- **不强区分**：公理、论文摘录、笔记、实验记录、甚至“验证通过的结论”——都可以作为 sources
- **组织自由**：你可以随意用子目录分组（例如 `papers/`、`notes/`、`axioms/`、`derived/`）
- **可沉淀**：当 multi-agent 共识 + 可执行验证通过时，结论也应该被写回成为新的 source（让系统越研究越强）

MVP 默认示例子目录：

- `axioms/`：仅作为一种组织方式（不是特殊语义）
- `references/`：同上


