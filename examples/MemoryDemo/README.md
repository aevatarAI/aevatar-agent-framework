## MemoryDemo（State.History / Compaction / Long-term Memory）

这个 demo 用一个最小 Web UI 展示 Aevatar 的 3 层记忆：

- **Layer 1**：`State.History`（短期窗口，可回放）
- **Layer 2**：`State.Context["history_summary"]`（滚动摘要，compaction 生成并注入 system prompt）
- **Layer 3**：`IAevatarAIMemory`（长期记忆，可选 DB；并通过内置工具 `search_memory` 按需检索）

---

### 运行

```bash
cd examples/MemoryDemo
dotnet run
```

打开：`http://localhost:5098`

---

### 配置 LLM（必须）

编辑 `examples/MemoryDemo/appsettings.secrets.json`：

- 把 `LLMProviders:Providers:deepseek:ApiKey` 改成你的 key
- 或者把 `Default/Providers` 改成你自己的 provider（OpenAI / AzureOpenAI / Ollama 等）

> `appsettings.json` 只放默认 provider 结构，不包含密钥。

---

### 可选：把长期记忆换成数据库（MongoDB / Supabase）

默认长期记忆是 **InMemory**（demo 自带 `InMemoryAIMemoryFactory`，无需外部依赖）。

如果配置了连接串，会自动切换为 DB（并覆盖 InMemory）：

- **MongoDB**：在 `appsettings.json` 或环境变量中配置
  - `ConnectionStrings:MongoDB = mongodb://localhost:27017`
  - `MongoDB:Database = aevatar`
- **Supabase(Postgres)**：配置
  - `ConnectionStrings:SupabasePostgres = Host=...;Username=...;Password=...;Database=...;`

---

### Demo 怎么触发 memory 效果

1. 连续聊多轮（超过 8 条 message）→ 会触发 compaction：
   - `State.History` 被裁剪到 8 条
   - `history_summary` 开始出现
   - 被裁掉的原文会写入 long-term memory（如果有 `IAevatarAIMemory`）
2. 用页面里的 **Search Memory** 搜一个“很早提到”的关键词：
   - 会同时查 `history_summary` / `State.History` / long-term memory（best-effort）


