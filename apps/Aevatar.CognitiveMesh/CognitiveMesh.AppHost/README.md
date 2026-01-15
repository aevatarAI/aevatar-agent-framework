# CognitiveMesh.AppHost

.NET Aspire AppHost for Cognitive Mesh - LLM Workflow Orchestration System.

## 🚀 Quick Start

```bash
# Navigate to AppHost directory
cd apps/CognitiveMesh.AppHost

# Run with Aspire
dotnet run
```

Aspire Dashboard will open automatically, providing:
- 📊 Distributed Tracing
- 📝 Structured Logs
- ❤️ Health Checks
- 📈 Metrics

## 📋 Services

| Service | Port | Description |
|---------|------|-------------|
| Cognitive Mesh | 5678 | Workflow orchestration with web UI |

## 🌐 Endpoints

After starting:

- **Web UI**: http://localhost:5678
- **Workflow Visualization**: http://localhost:5678/workflow.html
- **API**: http://localhost:5678/api/projects

## 🔧 Reasoning Strategies

| Strategy | Description |
|----------|-------------|
| `direct` | Direct LLM call |
| `uot` | Universe of Thought chain |
| `maker` | MAKER voting consensus |
| `cognitive` | Cognitive DSL workflows |

## 📦 Architecture

```
┌─────────────────────────────────────┐
│        Aspire Dashboard             │
│   (Tracing, Logs, Metrics)          │
└─────────────────────────────────────┘
                │
                ▼
┌─────────────────────────────────────┐
│      Cognitive Mesh Service         │
│  ┌─────────────────────────────┐    │
│  │   Web UI + Workflow Viz     │    │
│  ├─────────────────────────────┤    │
│  │   Strategy Engine           │    │
│  │   - Direct / UoT / MAKER    │    │
│  │   - Cognitive DSL           │    │
│  ├─────────────────────────────┤    │
│  │   LLM Providers             │    │
│  │   - DeepSeek / OpenAI       │    │
│  │   - Azure / Claude          │    │
│  └─────────────────────────────┘    │
└─────────────────────────────────────┘
```

## ⚙️ Configuration

LLM providers are configured via `llm-config.json` in the Cognitive Mesh project.

Example:
```json
{
  "deepseek": {
    "Provider": "deepseek",
    "Model": "deepseek-chat",
    "ApiKey": "sk-xxx"
  }
}
```

## 🔗 Related

- [Cognitive Mesh Documentation](../README.md)
- [MAKER System Design](../../../docs/maker/MAKER_SYSTEM.md)

