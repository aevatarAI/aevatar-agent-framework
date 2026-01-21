# Aevatar.Tools.Config

A CLI tool to configure Aevatar user secrets (LLM providers, API keys, etc.) via a local web UI.

## Installation

```bash
# Install from local build
dotnet pack tools/Aevatar.Tools.Config/Aevatar.Tools.Config.csproj
dotnet tool install --global --add-source ./tools/Aevatar.Tools.Config/bin/Release aevatar-config

# Or install from NuGet (when published)
dotnet tool install --global aevatar-config
```

## Update

```bash
# Rebuild and upgrade, remember to increase the version number
dotnet pack tools/Aevatar.Tools.Config/Aevatar.Tools.Config.csproj
dotnet tool update --global --add-source ./tools/Aevatar.Tools.Config/bin/Release aevatar-config
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
- Configure Web Search providers (Tavily / Brave / Bing / Serper)
- Generate secp256k1 keypairs for signing
- All secrets stored encrypted in `~/.aevatar/secrets.json`

## Security

- All API endpoints are localhost-only
- Secrets are encrypted at rest
- API keys are never exposed in logs or responses
