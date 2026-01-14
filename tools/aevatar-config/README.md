# aevatar-config

A CLI tool to configure Aevatar user secrets (LLM providers, API keys, etc.) via a local web UI.

## Installation

```bash
# Install from local build
dotnet pack tools/aevatar-config/aevatar-config.csproj
dotnet tool install --global --add-source ./tools/aevatar-config/bin/Release aevatar-config

# Or install from NuGet (when published)
dotnet tool install --global aevatar-config
```

## Usage

```bash
# Start the config server and open browser
aevatar-config

# Start without opening browser
aevatar-config --no-browser

# Use custom port
aevatar-config --port 8080
```

## Features

- Configure LLM provider API keys (OpenAI, Anthropic, DeepSeek, etc.)
- Manage multiple provider instances
- Test API key connectivity
- Generate secp256k1 keypairs for signing
- All secrets stored encrypted in `~/.aevatar/secrets.json`

## Security

- All API endpoints are localhost-only
- Secrets are encrypted at rest
- API keys are never exposed in logs or responses
