# AxiomReasoning.AppHost

.NET Aspire AppHost for **Aevatar.AxiomReasoning**（多智能体公理/定理推理平台）。

## 🚀 Quick Start

```bash
cd apps/AxiomReasoning.AppHost
dotnet run
```

## 📋 Services

| Service | Port | Description |
|--------|------|-------------|
| Axiom Reasoning | 5001 | Web UI + SSE progress for theorem discovery loop |

## 🌐 Endpoints

- **Web UI**: `http://localhost:5001`
- **Health**: `http://localhost:5001/health`
- **API**: `http://localhost:5001/api/sessions`
- **SSE**: `http://localhost:5001/api/sessions/{sessionId}/events`

## 🔗 Related

- `cognitive-mesh/Aevatar.AxiomReasoning/README.md`
- `src/Aevatar.Agents.Cognitive/workflows/axiom_theorem_loop.yaml`

## 📊 Aspire Dashboard

Dashboard 会自动打开（并在终端输出登录 URL）。

### 常见问题：Dashboard 打不开 / gRPC 报 UntrustedRoot

你贴的错误：
- `Grpc.Core.RpcException ... AuthenticationException ... UntrustedRoot`

原因是 **Aspire Resource Service / Dashboard gRPC 使用 HTTPS 的自签证书**，本机未信任开发证书链。

**方案 A（推荐，最快止血）**：用 http profile（禁用 TLS）

```bash
cd apps/AxiomReasoning.AppHost
dotnet run --launch-profile http
```

该 profile 已包含：
- `DOTNET_RESOURCE_SERVICE_ENDPOINT_URL=http://localhost:20380`
- `DOTNET_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:19380`
- `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`

**方案 B（长期正确做法）**：信任 dev HTTPS 证书，然后用 https profile

```bash
dotnet dev-certs https --clean
dotnet dev-certs https --trust
cd apps/AxiomReasoning.AppHost
dotnet run --launch-profile https
```

> macOS 上 `--trust` 可能会弹 Keychain 授权提示；若仍失败，去 Keychain Access 里把 “ASP.NET Core HTTPS development certificate” 设为 Always Trust。

