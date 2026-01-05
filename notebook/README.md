## Aevatar.Notebook (NotebookLM-lite MVP)

目标：做一个类似 NotebookLM 的三栏应用：

- 左侧：资料/记忆管理（Sources）
- 中间：问答（Q&A）
- 右侧：报告生成（Report）

并尽可能复用 Aevatar Agent Framework 的 Memory 体系（见 `docs/AI_MEMORY_GUIDE.md`）。

---

### Run（推荐）

```bash
./notebook/start.sh
```

- Notebook UI: `http://localhost:5678`

---

### 目录结构（规范化后）

```
notebook/
├── start.sh
├── src/
│   ├── Aevatar.Notebook/           # Core
│   └── Aevatar.Notebook.Api/       # API Host
└── Aevatar.Notebook.AppHost/       # Aspire AppHost
```

### AG-UI（前后端通信）

- 说明：当前 UI（`wwwroot/`）仍保留原来的 NDJSON streaming 协议；AG‑UI 端点用于标准化客户端/未来新 UI。
- 创建 session：`POST /api/sessions`
- 提交输入：`POST /api/sessions/{id}/input`
- SSE（AG-UI）：`GET /api/sessions/{id}/agui/events`

断线重连采用 **snapshot-first**：先发 `MESSAGES_SNAPSHOT` 再进入 live stream。

---

### Persistence Switch（config-driven）

编辑 `notebook/src/Aevatar.Notebook.Api/appsettings.json`：

- `Aevatar:Persistence:MemoryStore`: `file | mongodb | supabase`
- `Aevatar:Persistence:MemoryVectorIndex`: `file | mongodb | supabase`
- `Aevatar:Persistence:MemoryGraph`: `file | neo4j`

数据库连接信息建议放到 `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json`（已被 `.gitignore` 忽略）。
可直接复制示例：

- `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json.example`

---

### MVP 当前行为

- **Sources**：通过 `POST /api/sources/text` 写入 `IMemoryStore`（scopeType=Graph，memoryId=`source::<id>`）
- **Chat**：每次问答会把所有 sources（有界截断）拼成 `notebook_context` 注入 system prompt，确保“上下文都喂给 LLM”
- **Report**：同样基于 `notebook_context` 生成结构化报告

---

### LLM 配置与超时排障（DeepSeek / OpenAI‑compatible）

`Aevatar.Notebook` 读取 `LLMProviders` 配置（建议把 API Key 放到 `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json`），并用 `LLMProviders:Default` 作为默认 provider。

#### 配置示例

把下面内容放到 `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json`（或环境变量注入同名配置）：

```json
{
  "LLMProviders": {
    "Default": "deepseek",
    "Providers": {
      "deepseek": {
        "ProviderType": "OpenAI",
        "ApiKey": "${DEEPSEEK_API_KEY}",
        "Endpoint": "https://YOUR_OPENAI_COMPAT_ENDPOINT",
        "Model": "deepseek-chat",
        "Temperature": 0.2,
        "MaxTokens": 1200,
        "TimeoutMilliseconds": 900000
      }
    }
  }
}
```

说明：
- **`ProviderType`**：除 `AzureOpenAI` 以外都走 OpenAI‑compatible 客户端（包含 DeepSeek / DashScope 等）。
- **`TimeoutMilliseconds`**：单次调用超时（同时驱动网络层 `NetworkTimeout` 与框架层 `CallTimeout`）。默认已放宽到 **10 分钟**，你也可以对特定 provider 进一步调大。

#### 快速定位：看 `/api/info`

启动后访问 `http://localhost:5678/api/info`，会返回当前默认 provider 的非敏感诊断信息（`providerType/model/endpoint/timeoutMilliseconds`），以及当前 persistence 选择。

#### 常见错误：`TaskCanceledException` / “The operation was canceled.”

这通常表示 **LLM 请求超时或网络被中断**。Notebook 现在会对这类情况返回 **504**，并提示你该调哪里：
- 调大：`LLMProviders:Providers:<provider>:TimeoutMilliseconds`
- 检查：`Endpoint` 是否可达、网络/代理是否稳定、Key 是否正确

（可选）如果你想减少网络层日志噪音：`notebook/src/Aevatar.Notebook.Api/appsettings.json` 已把 `System.ClientModel*` 调到 `Warning`。


