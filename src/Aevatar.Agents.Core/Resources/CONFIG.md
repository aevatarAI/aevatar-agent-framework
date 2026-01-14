# Aevatar Configuration Guide

This directory (`~/.aevatar/`) stores your Aevatar Agent Framework configuration.

## Quick Start

### 1. Configure LLM Provider

Create `secrets.json` with your API key:

```json
{
  "LLMProviders": {
    "Providers": {
      "openai": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-your-api-key-here",
        "Model": "gpt-4o"
      }
    }
  }
}
```

### 2. Run an Aevatar Application

Your configuration is automatically loaded. No need to set environment variables or copy config files.

---

## Directory Structure

```
~/.aevatar/
├── CONFIG.md        # This file
├── secrets.json     # API Keys (encrypted at rest)
├── config.json      # Non-sensitive settings (optional)
├── agents/          # Agent YAML configs
├── skills/          # Reusable skill documents
├── workflows/       # Workflow definitions
└── mcp/             # MCP server configs
```

---

## secrets.json

Store all sensitive data here. Format follows .NET `appsettings.json` conventions.

### Full Example

```json
{
  "LLMProviders": {
    "Default": "openai",
    "Providers": {
      "openai": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-...",
        "Model": "gpt-4o",
        "Temperature": 0.7,
        "MaxTokens": 4096
      },
      "azure": {
        "ProviderType": "AzureOpenAI",
        "ApiKey": "...",
        "Endpoint": "https://your-resource.openai.azure.com",
        "DeploymentName": "gpt-4",
        "Model": "gpt-4"
      },
      "deepseek": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-...",
        "Endpoint": "https://api.deepseek.com",
        "Model": "deepseek-chat"
      },
      "ollama": {
        "ProviderType": "Ollama",
        "Endpoint": "http://localhost:11434",
        "Model": "llama3.2"
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
    "Tavily": "tvly-...",
    "Exa": "...",
    "Aliyun": {
      "AccessKeyId": "LTAI...",
      "AccessKeySecret": "..."
    }
  },
  "MCP": {
    "Servers": {
      "github": {
        "Token": "ghp_..."
      }
    }
  }
}
```

### Provider Types

| ProviderType | Description | Required Fields |
|-------------|-------------|-----------------|
| `OpenAI` | OpenAI API (and compatible) | `ApiKey`, `Model` |
| `AzureOpenAI` | Azure OpenAI Service | `ApiKey`, `Endpoint`, `DeploymentName` |
| `Anthropic` | Claude models | `ApiKey`, `Model` |
| `Ollama` | Local Ollama | `Endpoint`, `Model` |

### ServiceApiKeys

Add any API key you need. No code changes required:

```json
{
  "ServiceApiKeys": {
    "MyNewService": "my-api-key",
    "ComplexService": {
      "ClientId": "...",
      "ClientSecret": "..."
    }
  }
}
```

Access in code:

```csharp
var key = configuration["ServiceApiKeys:MyNewService"];
var clientId = configuration["ServiceApiKeys:ComplexService:ClientId"];
```

---

## config.json (Optional)

Store non-sensitive settings here. This file is NOT encrypted.

```json
{
  "Aevatar": {
    "Agents": {
      "ParallelLimit": 3
    },
    "Tools": {
      "Shell": {
        "AllowedCommands": ["git", "npm", "cargo", "dotnet"],
        "TimeoutSeconds": 120
      }
    },
    "Logging": {
      "Level": "Information"
    }
  },
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

**Note**: If both `secrets.json` and `config.json` define the same key, `secrets.json` takes priority.

---

## Configuration Priority

From lowest to highest:

1. `~/.aevatar/config.json`
2. `~/.aevatar/secrets.json`
3. `appsettings.json` (project)
4. `appsettings.{Environment}.json`
5. `appsettings.secrets.json` (project)
6. Environment Variables

---

## Security

1. **File Permissions**: Recommended `chmod 600 ~/.aevatar/secrets.json`
2. **Encryption**: `secrets.json` is encrypted at rest using AES-256-GCM
3. **Key Storage**: Master key stored in macOS Keychain (or file fallback)
4. **Never Commit**: Add `.aevatar/` to your global `.gitignore`

```bash
# Set proper permissions
chmod 700 ~/.aevatar
chmod 600 ~/.aevatar/secrets.json
chmod 600 ~/.aevatar/config.json
```

---

## Environment Variables

Override default paths:

| Variable | Description |
|----------|-------------|
| `AEVATAR_SECRETS_PATH` | Full path to secrets.json |
| `AEVATAR_SECRETS_DIR` | Directory containing config files |

---

## Troubleshooting

### API Key Not Found

1. Check `secrets.json` syntax (valid JSON?)
2. Verify provider name matches (case-sensitive)
3. Ensure `LLMProviders:Default` points to valid provider

### Permission Denied

```bash
chmod 600 ~/.aevatar/secrets.json
```

### Reset Encryption

If master key is lost:

```bash
rm ~/.aevatar/masterkey.bin
rm ~/.aevatar/secrets.json
# Then recreate secrets.json
```

---

## More Information

- Full documentation: https://github.com/aevatarAI/aevatar-agent-framework
- API reference: See `docs/AEVATAR_CONFIG.md` in the repository

---

*Aevatar Agent Framework*
