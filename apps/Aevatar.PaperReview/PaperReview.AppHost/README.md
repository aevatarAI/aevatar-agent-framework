# PaperReview.AppHost

.NET Aspire AppHost for Paper Review - AI-Powered Academic Paper Review Platform.

## 🚀 Quick Start

```bash
# Navigate to AppHost directory
cd apps/PaperReview.AppHost

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
| Paper Review | 5002 | AI academic paper review with web UI |

## 🌐 Endpoints

After starting:

- **Web UI**: http://localhost:5002
- **API**: http://localhost:5002/api/sessions

## 🎯 Review Types

| Type | Description |
|------|-------------|
| Quick Review | 快速预审 |
| Detailed Review | 详细评审 |
| Deep Analysis | 深度分析 |
| Revision Suggestion | 修改建议 |

## 🏛️ Supported Venues

| Category | Venues |
|----------|--------|
| AI Conference | NeurIPS, ICML, ICLR |
| NLP Conference | ACL, EMNLP, NAACL |
| CV Conference | CVPR, ICCV, ECCV |
| Journal | JMLR, TPAMI |

## 📦 Architecture

```
┌─────────────────────────────────────┐
│        Aspire Dashboard             │
│   (Tracing, Logs, Metrics)          │
└─────────────────────────────────────┘
                │
                ▼
┌─────────────────────────────────────┐
│      Paper Review Service           │
│  ┌─────────────────────────────┐    │
│  │   Web UI                    │    │
│  ├─────────────────────────────┤    │
│  │   MAKER Consensus System    │    │
│  │   - Multi-expert voting     │    │
│  │   - Consensus aggregation   │    │
│  ├─────────────────────────────┤    │
│  │   LLM Providers             │    │
│  │   - DeepSeek / OpenAI       │    │
│  │   - Azure / Claude          │    │
│  └─────────────────────────────┘    │
└─────────────────────────────────────┘
```

## ⚙️ Configuration

LLM providers configured via `appsettings.secrets.json`:

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

## 🔗 Related

- [Paper Review Documentation](../src/Aevatar.PaperReview/README.md)
- [Cognitive Mesh](../../Aevatar.CognitiveMesh/README.md)
