# Aevatar.Notebook — Development

## One-click (recommended)

```bash
./experimental/notebook/boot.sh
```

- Notebook UI / API: `http://localhost:5678`

## Aspire AppHost

```bash
dotnet run --project experimental/notebook/Aevatar.Notebook.AppHost/Aevatar.Notebook.AppHost.csproj
```

## Run backend only

```bash
dotnet run --project experimental/notebook/src/Aevatar.Notebook.Api/Aevatar.Notebook.Api.csproj
```

## Common Troubleshooting

- **Port in use**：用 `./experimental/notebook/boot.sh`（默认会 kill 端口），或设置不同端口：
  - `API_PORT=5679 ./experimental/notebook/boot.sh`
- **LLM 调用超时**：调大 `LLMProviders:Providers:<name>:TimeoutMilliseconds`
- **找不到 provider / missing ApiKey**：检查 `~/.aevatar/secrets.json`（推荐）或 `experimental/notebook/src/Aevatar.Notebook.Api/appsettings.secrets.json`


