# Maker System - Aspire Host

Aspire-integrated hosting for the Maker multi-agent system, providing enhanced observability and monitoring.

## Features

| Capability | Description |
|------------|-------------|
| **Distributed Tracing** | Track Task → Coordinator → Worker → LLM call chains |
| **Log Aggregation** | Centralized structured logging from all agents |
| **Metrics** | Agent events, LLM latency, queue depth |
| **Health Checks** | Real-time service health monitoring |
| **Service Graph** | Visualize agent dependencies |

## Quick Start

```bash
cd apps/Aevatar.MakerSystem/MakerSystem.AppHost
dotnet run --launch-profile http
```

## Access Points

After startup, check terminal output for URLs:

```
Login to the dashboard at http://localhost:18080/login?t=<token>
```

- **Aspire Dashboard**: `http://localhost:18080/login?t=<token>` (token in terminal output)
- **Maker UI**: Dynamically assigned port (visible in Dashboard Resources tab)
- **Health Check**: `http://<maker-url>/health`

## What You Can Monitor

### 1. Distributed Traces
Track complete execution flow:
```
[Task Created] → [Coordinator Dispatch] → [Worker Execute] → [LLM Call] → [Result]
```

### 2. Structured Logs
Filter by:
- Agent ID
- Task ID
- Event Type
- Severity Level

### 3. Metrics
- `aevatar.agents.events.*` - Event publishing/processing counts
- `aevatar.agents.active.count` - Active agent count
- HTTP request latency
- Runtime metrics (GC, threads, etc.)

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    MakerSystem.AppHost                        │
│  ┌────────────────────────────────────────────────────────┐  │
│  │                   Aspire Dashboard                      │  │
│  │  • Traces  • Logs  • Metrics  • Health                 │  │
│  └────────────────────────────────────────────────────────┘  │
│                            ↑ OTLP                             │
│  ┌────────────────────────────────────────────────────────┐  │
│  │                    MakerSystem                          │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │  │
│  │  │ Coordinator │──│   Worker    │──│  LLM Call   │     │  │
│  │  │   Agent     │  │   Agents    │  │  (MEAI)     │     │  │
│  │  └─────────────┘  └─────────────┘  └─────────────┘     │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

## Configuration

OpenTelemetry is auto-configured when running under Aspire. The following environment variables are set automatically:

- `OTEL_EXPORTER_OTLP_ENDPOINT` - Dashboard OTLP endpoint
- `OTEL_SERVICE_NAME` - Service name for traces

## Troubleshooting

### Dashboard Not Accessible
- Ensure Aspire SDK is installed: `dotnet workload install aspire`
- Trust dev certificate: `dotnet dev-certs https --trust`

### No Traces Showing
- Verify `OTEL_EXPORTER_OTLP_ENDPOINT` is set (auto-set by Aspire)
- Check MakerSystem logs for OTLP export errors

### Health Check Failing
- Ensure `/health` endpoint is accessible
- Check MakerSystem startup logs for errors

