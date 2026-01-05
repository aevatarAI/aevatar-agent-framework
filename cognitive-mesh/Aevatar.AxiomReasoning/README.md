# Aevatar.AxiomReasoning

> 多智能体公理推理 Demo（基于 Cognitive Mesh），使用 `axiom_reasoning.yaml` 工作流实现“逐步推理 + 每步共识”。

## 运行

1. 配置 LLM（参考 `appsettings.secrets.json`）：

```json
{
  "LLMProviders": {
    "Providers": {
      "default": {
        "Name": "default",
        "ProviderKind": "DeepSeek",
        "ModelId": "deepseek-chat",
        "ApiKey": "your-api-key"
      }
    }
  }
}
```

2. 启动：

```bash
cd cognitive-mesh/Aevatar.AxiomReasoning
dotnet run
```

3. 访问：

- Web UI: `http://localhost:5001`
- Health: `http://localhost:5001/health`

## 核心点

- **工作流**：`src/Aevatar.Agents.Cognitive/workflows/axiom_reasoning.yaml`
- **策略执行**：`CognitiveStrategy.ExecuteAsync(..., CognitiveWorkflow="axiom_reasoning")`
- **实时事件**：
  - Legacy SSE：`/api/sessions/{id}/events`
  - **AG-UI SSE（推荐）**：`/api/sessions/{id}/agui/events`（标准 AG-UI 事件 + CUSTOM 扩展）
    - 连接时先发 `MESSAGES_SNAPSHOT(基于 State.History)` / `aevatar.axiom.status_snapshot` / `STATE_SNAPSHOT(可选)`，之后进入 live stream（不依赖 replay）


