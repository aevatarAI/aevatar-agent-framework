# Aevatar 配置系统设计

> `~/.aevatar/` 目录结构与配置规范
> 
> **设计原则**: 配置格式与 .NET `appsettings.json` 风格一致，便于复用 .NET Configuration 加载逻辑

---

## 目录结构

```
~/.aevatar/
├── CONFIG.md                # 配置说明文档 (用户参考)
├── secrets.json             # 敏感信息 (API Keys, 加密存储)
├── config.json              # 非敏感配置 (明文, 可选)
├── agents/                  # Agent 配置
│   ├── coder.yaml
│   ├── reviewer.yaml
│   └── ...
├── skills/                  # Agent Skills (Claude Code 风格)
│   ├── commit.md
│   ├── code-review.md
│   └── ...
├── workflows/               # Cognitive Mesh DSL 工作流
│   ├── code-review.json
│   └── maker.json
├── mcp/                     # MCP 服务器配置
│   └── servers.json
└── logs/                    # 日志目录 (自动创建)
```

---

## 配置优先级

配置加载顺序 (后面的覆盖前面的):

```
1. ~/.aevatar/config.json      (最低优先级, 用户全局配置)
2. ~/.aevatar/secrets.json     (用户敏感配置, 加密)
3. appsettings.json            (项目配置)
4. appsettings.{Environment}.json
5. appsettings.secrets.json    (项目敏感配置, 最高优先级)
6. Environment Variables
```

**使用方式**:

```csharp
builder.Configuration
    .AddAevatarUserConfig()    // 加载 ~/.aevatar/config.json + secrets.json
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{env}.json", optional: true)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();
```

---

## secrets.json (敏感配置)

**用途**: 存储 API Keys 等敏感信息，加密存储在磁盘上。

### 结构 (appsettings.json 风格)

```json
{
  "LLMProviders": {
    "Default": "openai",
    "Providers": {
      "openai": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-...",
        "Model": "gpt-4o"
      },
      "azure": {
        "ProviderType": "AzureOpenAI",
        "ApiKey": "...",
        "Endpoint": "https://your-resource.openai.azure.com",
        "DeploymentName": "gpt-4"
      },
      "deepseek": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-...",
        "Endpoint": "https://api.deepseek.com",
        "Model": "deepseek-chat"
      }
    },
    "Embeddings": {
      "ProviderType": "AzureOpenAI",
      "ApiKey": "...",
      "Endpoint": "https://your-embedding.openai.azure.com",
      "DeploymentName": "text-embedding-3-small"
    }
  },
  "ServiceApiKeys": {
    "GitHub": "ghp_...",
    "SkillsMp": "sk-skillsmp-...",
    "Aliyun": {
      "AccessKeyId": "LTAI...",
      "AccessKeySecret": "..."
    },
    "Tavily": "tvly-...",
    "Exa": "...",
    "Custom": {
      "MyService": "my-api-key"
    }
  },
  "MCP": {
    "Servers": {
      "github": {
        "Token": "ghp_..."
      },
      "filesystem": {
        "AllowedPaths": ["~/Code", "/tmp"]
      }
    }
  }
}
```

### LLMProviders 字段说明

| 字段 | 必填 | 说明 |
|------|------|------|
| `LLMProviders:Default` | ❌ | 默认 provider 名称 |
| `LLMProviders:Providers:<name>:ProviderType` | ✅ | 类型: OpenAI, AzureOpenAI, Anthropic, Ollama |
| `LLMProviders:Providers:<name>:ApiKey` | 视类型 | API Key (Ollama 不需要) |
| `LLMProviders:Providers:<name>:Endpoint` | ❌ | API 端点 (有默认值) |
| `LLMProviders:Providers:<name>:Model` | ❌ | 默认模型名称 |
| `LLMProviders:Providers:<name>:DeploymentName` | Azure | Azure OpenAI 部署名称 |
| `LLMProviders:Embeddings` | ❌ | 全局 Embedding 配置 |

### ServiceApiKeys 字段说明

`ServiceApiKeys` 是一个**开放式字典**，可以添加任意服务的 API Key:

| 字段 | 说明 | 示例 |
|------|------|------|
| `ServiceApiKeys:GitHub` | GitHub Personal Access Token | `ghp_xxxx` |
| `ServiceApiKeys:SkillsMp` | SkillsMp API Key | `sk-skillsmp-xxxx` |
| `ServiceApiKeys:Tavily` | Tavily Search API Key | `tvly-xxxx` |
| `ServiceApiKeys:Exa` | Exa Search API Key | `...` |
| `ServiceApiKeys:Aliyun:AccessKeyId` | 阿里云 Access Key ID | `LTAI...` |
| `ServiceApiKeys:Aliyun:AccessKeySecret` | 阿里云 Access Key Secret | `...` |
| `ServiceApiKeys:Custom:<name>` | 自定义服务 | 任意值 |

**扩展新服务**: 直接在 `ServiceApiKeys` 下添加新 key，无需修改代码:

```json
{
  "ServiceApiKeys": {
    "MyNewService": "my-api-key",
    "AnotherService": {
      "Key": "...",
      "Secret": "..."
    }
  }
}
```

代码中读取:

```csharp
var myKey = configuration["ServiceApiKeys:MyNewService"];
var anotherKey = configuration["ServiceApiKeys:AnotherService:Key"];
```

### Provider 解析规则

1. **显式指定**: Agent 配置中指定 `provider: "deepseek"` → 使用 deepseek
2. **未指定时**: 使用 `LLMProviders:Default` 字段值
3. **无 Default**: 使用 `Providers` 中的第一个
4. **保证**: 只要配置了至少一个 provider，就永远不会找不到模型

```
Agent.provider = "deepseek"  →  Providers["deepseek"]
Agent.provider = null        →  Providers[Default]
Default = null               →  Providers.First()
```

---

## Agent 配置 (agents/*.yaml)

### 简化后的结构

```yaml
# ~/.aevatar/agents/coder.yaml
id: "coder"
name: "Coder Agent"

# Provider 配置
provider: "default"          # "default" = 使用 secrets.json 的默认 provider
model: null                  # null = 使用 provider 的 default_model

# 模型参数 (可选)
temperature: 0.3
max_tokens: 8192

# Persona
persona:
  role: "资深软件工程师"
  expertise: ["代码实现", "调试", "重构"]
  style: "专业、高效"

# 工具列表
tools:
  - file_read
  - file_write
  - bash
  - git

# Skills (引用 ~/.aevatar/skills/ 下的文件)
skills:
  - "commit"
  - "code-review"

# System Prompt
system_prompt: |
  你是一位资深软件工程师。
  
  工作原则：
  1. 先理解需求，再动手编码
  2. 遵循项目现有的代码风格
  3. 编写必要的注释
```

### 最简配置

```yaml
# 最简配置 - 使用所有默认值
id: "simple"
name: "Simple Agent"
# provider: 默认为 "default"
# model: 默认为 null (使用 provider 的 default_model)
```

### 字段说明

| 字段 | 必填 | 默认值 | 说明 |
|------|------|--------|------|
| `id` | ✅ | - | Agent 唯一标识 |
| `name` | ❌ | = id | 显示名称 |
| `provider` | ❌ | "default" | "default" 或具体 provider 名称 |
| `model` | ❌ | null | 模型名称，null 时使用 provider.default_model |
| `temperature` | ❌ | null | 温度参数，null 时使用 provider 默认值 |
| `max_tokens` | ❌ | null | 最大输出 token，null 时使用 provider 默认值 |
| `persona` | ❌ | - | Agent 人设 |
| `tools` | ❌ | [] | 可用工具列表 |
| `skills` | ❌ | [] | 引用的 skill 文件名 (不含 .md) |
| `system_prompt` | ❌ | - | 系统提示词 |

---

## skills/ 目录

Agent Skills 是可复用的指令模板，类似 Claude Code 的 custom instructions。

### 目录结构

```
~/.aevatar/skills/
├── commit.md              # Git 提交规范
├── code-review.md         # 代码审查标准
├── refactor.md            # 重构指南
├── testing.md             # 测试编写规范
└── custom/                # 自定义 skills
    └── my-project.md
```

### Skill 文件格式

```markdown
# Skill: Code Review

## 触发条件
当用户要求审查代码时激活。

## 指令

审查代码时，关注以下方面：

1. **正确性**: 代码是否实现了预期功能？
2. **可读性**: 命名是否清晰？逻辑是否易懂？
3. **性能**: 是否有明显的性能问题？
4. **安全性**: 是否有安全漏洞？
5. **测试**: 是否有足够的测试覆盖？

## 输出格式

使用以下格式输出审查结果：

- ✅ 优点: ...
- ⚠️ 建议: ...
- ❌ 问题: ...
```

### 在 Agent 中引用 Skill

```yaml
# agents/reviewer.yaml
id: "reviewer"
name: "Code Reviewer"
skills:
  - "code-review"
  - "custom/my-project"
```

---

## config.json (非敏感配置)

**用途**: 存储非敏感的全局配置，明文存储。如果用户不担心泄露，也可以在这里配置 LLM Providers。

### 结构 (appsettings.json 风格)

```json
{
  "Aevatar": {
    "Version": "1.0",
    "Agents": {
      "DefaultWorkflow": "standard",
      "ParallelLimit": 3
    },
    "Tools": {
      "Shell": {
        "AllowedCommands": ["git", "npm", "cargo", "dotnet"],
        "TimeoutSeconds": 120
      },
      "Filesystem": {
        "AllowedPaths": ["~/Code", "/tmp"]
      }
    },
    "UI": {
      "Theme": "dark",
      "Editor": "vscode"
    },
    "Logging": {
      "Level": "Information"
    }
  },
  "LLMProviders": {
    "Default": "ollama",
    "Providers": {
      "ollama": {
        "ProviderType": "Ollama",
        "Endpoint": "http://localhost:11434",
        "Model": "llama3.2"
      }
    }
  }
}
```

**注意**: 如果 `secrets.json` 和 `config.json` 都配置了同一个 key，`secrets.json` 优先。

---

## Provider 解析流程

```
┌─────────────────────────────────────────────────────────────┐
│                    Agent 请求 Provider                        │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
                    ┌─────────────────┐
                    │ agent.provider  │
                    │ = "default" ?   │
                    └─────────────────┘
                      │           │
                    是 "default"   具体名称
                      │           │
                      ▼           ▼
            ┌─────────────────┐  ┌─────────────┐
            │ LLMProviders:   │  │ 使用指定的   │
            │ Default 有值吗？ │  │ provider    │
            └─────────────────┘  └─────────────┘
                │           │           │
               有          没有         │
                │           │           │
                ▼           ▼           │
      ┌─────────────┐  ┌─────────────┐  │
      │ 使用 Default │  │ 使用第一个   │  │
      │ provider    │  │ provider    │  │
      └─────────────┘  └─────────────┘  │
                │           │           │
                └─────┬─────┴───────────┘
                      │
                      ▼
            ┌─────────────────┐
            │ 从 Providers[]  │
            │ 获取完整配置    │
            └─────────────────┘
                      │
                      ▼
        ┌─────────────────────────────┐
        │ agent.model != null ?       │
        │   是 → 覆盖 provider.Model  │
        │   否 → 使用 provider.Model  │
        └─────────────────────────────┘
```

---

## 示例场景

### 场景 1: 单 Provider 用户

```json
// ~/.aevatar/secrets.json
{
  "LLMProviders": {
    "Providers": {
      "openai": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-...",
        "Model": "gpt-4o"
      }
    }
  }
}
```

```yaml
# ~/.aevatar/agents/coder.yaml
id: "coder"
name: "Coder"
# 不需要指定 provider，自动使用 openai
```

### 场景 2: 多 Provider 用户

```json
// ~/.aevatar/secrets.json
{
  "LLMProviders": {
    "Default": "deepseek",
    "Providers": {
      "openai": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-...",
        "Model": "gpt-4o"
      },
      "deepseek": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-...",
        "Endpoint": "https://api.deepseek.com",
        "Model": "deepseek-chat"
      }
    }
  },
  "ServiceApiKeys": {
    "GitHub": "ghp_..."
  }
}
```

```json
// ~/.aevatar/config.json (非敏感配置分离)
{
  "LLMProviders": {
    "Providers": {
      "ollama": {
        "ProviderType": "Ollama",
        "Endpoint": "http://localhost:11434",
        "Model": "llama3.2"
      }
    }
  }
}
```

```yaml
# agents/coder.yaml - 使用 deepseek (default)
id: "coder"
name: "Coder"

# agents/reviewer.yaml - 显式使用 openai
id: "reviewer"
name: "Reviewer"
provider: "openai"
model: "gpt-4o"

# agents/local.yaml - 使用本地 ollama
id: "local"
name: "Local Agent"
provider: "ollama"
```

### 场景 3: 使用 ServiceApiKeys

```json
// ~/.aevatar/secrets.json
{
  "ServiceApiKeys": {
    "GitHub": "ghp_...",
    "SkillsMp": "sk-skillsmp-...",
    "Aliyun": {
      "AccessKeyId": "LTAI...",
      "AccessKeySecret": "..."
    },
    "Tavily": "tvly-...",
    "MyCustomService": "my-api-key"
  }
}
```

代码中使用:

```csharp
// 直接从 IConfiguration 读取
var githubToken = configuration["ServiceApiKeys:GitHub"];
var aliyunKeyId = configuration["ServiceApiKeys:Aliyun:AccessKeyId"];
var myKey = configuration["ServiceApiKeys:MyCustomService"];

// 或使用 Options 模式
services.Configure<ServiceApiKeysOptions>(configuration.GetSection("ServiceApiKeys"));
```

### 场景 4: 覆盖模型参数

```yaml
# agents/creative.yaml
id: "creative"
name: "Creative Agent"
provider: "openai"
model: "gpt-4o"
temperature: 1.2        # 更高的创造性
max_tokens: 16384       # 更长的输出
```

---

## 安全注意事项

1. **secrets.json** 加密存储，但仍建议添加到 `.gitignore`
2. 文件权限建议设为 `600` (仅所有者可读写)
3. 不要在 agent yaml 中硬编码 API Key
4. 支持环境变量替换: `"ApiKey": "${OPENAI_API_KEY}"`

```bash
chmod 600 ~/.aevatar/secrets.json
```

---

## 迁移指南

如果从旧配置迁移:

1. 将所有 API Key 移到 `secrets.json` 的 `LLMProviders:Providers` 或 `ServiceApiKeys`
2. Agent yaml 中删除 `api_key` 字段
3. 将完整的 model config 替换为 `provider` 引用

**Before (旧格式):**
```yaml
model:
  provider: "openai"
  api_key: "sk-..."
  name: "gpt-4"
  endpoint: "https://api.openai.com/v1"
```

**After (新格式):**
```yaml
provider: "openai"
model: "gpt-4"
```

---

*最后更新: 2025-01-14*
