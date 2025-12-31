## Aevatar.Notebook (NotebookLM-lite MVP)

目标：做一个类似 NotebookLM 的三栏应用：

- 左侧：资料/记忆管理（Sources）
- 中间：问答（Q&A）
- 右侧：报告生成（Report）

并尽可能复用 Aevatar Agent Framework 的 Memory 体系（见 `docs/AI_MEMORY_GUIDE.md`）。

---

### Run

```bash
cd notebook
dotnet run
```

Open: `http://localhost:5099`

---

### Persistence Switch (config-driven)

编辑 `notebook/appsettings.json`：

- `Aevatar:Persistence:MemoryStore`: `file | mongodb | supabase`
- `Aevatar:Persistence:MemoryVectorIndex`: `file | mongodb | supabase`
- `Aevatar:Persistence:MemoryGraph`: `file | neo4j`

数据库连接信息建议放到 `notebook/appsettings.secrets.json`（已被 `.gitignore` 忽略）。

---

### MVP 当前行为

- **Sources**：通过 `POST /api/sources/text` 写入 `IMemoryStore`（scopeType=Graph，memoryId=`source::<id>`）
- **Chat**：每次问答会把所有 sources（有界截断）拼成 `notebook_context` 注入 system prompt，确保“上下文都喂给 LLM”
- **Report**：同样基于 `notebook_context` 生成结构化报告


