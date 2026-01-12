# Aevatar.Notebook — Development

## One-click (recommended)

```bash
./notebook/start.sh
```

- Notebook UI / API: `http://localhost:5678`

## Aspire AppHost

```bash
dotnet run --project notebook/Aevatar.Notebook.AppHost/Aevatar.Notebook.AppHost.csproj
```

## Run backend only

```bash
dotnet run --project notebook/src/Aevatar.Notebook.Api/Aevatar.Notebook.Api.csproj
```

## Common Troubleshooting

- **Port in use**：用 `./notebook/start.sh`（默认会 kill 端口），或设置不同端口：
  - `API_PORT=5679 ./notebook/start.sh`
- **LLM 调用超时**：调大 `LLMProviders:Providers:<name>:TimeoutMilliseconds`
- **找不到 provider / missing ApiKey**：检查 `~/.aevatar/secrets.json`（推荐）或 `notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json`


