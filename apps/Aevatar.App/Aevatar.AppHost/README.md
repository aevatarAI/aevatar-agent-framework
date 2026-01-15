# Aevatar.AppHost

Aspire orchestration entry point for the Aevatar Agent Platform distributed system.

## Quick Start

```bash
# 1. Start infrastructure (MongoDB, Redis, Kafka)
cd apps/Aevatar.App/Aevatar.AppHost
docker-compose up -d

# 2. Wait for services to be healthy
docker-compose ps

# 3. Run Aspire AppHost
dotnet run
```

## Services

| Service | Description | Default Port |
|---------|-------------|--------------|
| **silo** | Orleans Silo - Agent runtime | 8081 (health) |
| **auth-server** | OpenIddict + ABP Identity | 5001 |
| **api-host** | ABP REST API | 5000 |

## Infrastructure (docker-compose)

| Resource | Type | Port | Purpose |
|----------|------|------|---------|
| **mongodb** | MongoDB 7.0 | 27017 | State persistence |
| **redis** | Redis 7 | 6379 | Distributed caching |
| **kafka** | Kafka | 29092 | Event streaming |
| **zookeeper** | Zookeeper | 2181 | Kafka coordination |
| **kafka-ui** | Kafka UI | 8082 | Monitoring dashboard |

## Dashboard & Monitoring

After startup:

- **Aspire Dashboard**: Auto-opens in browser
  - Distributed Tracing
  - Structured Logs
  - Health Checks
  - Performance Metrics

- **Kafka UI**: http://localhost:8082
  - Topic management
  - Consumer groups
  - Message browsing

## Ports

| Port | Service |
|------|---------|
| 19080 | AppHost HTTP |
| 19443 | AppHost HTTPS |
| 19889 | OTLP Endpoint |
| 19890 | Resource Service |

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Aspire Dashboard                         │
│            (Traces, Logs, Metrics, Health)                  │
└─────────────────────────────────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
┌───────────────┐   ┌─────────────────┐   ┌───────────────┐
│   API Host    │   │   Auth Server   │   │     Silo      │
│  (ABP REST)   │   │  (OpenIddict)   │   │   (Orleans)   │
└───────┬───────┘   └────────┬────────┘   └───────┬───────┘
        │                    │                    │
        └────────────────────┴────────────────────┘
                             │
     ┌───────────────────────┼───────────────────────┐
     │                       │                       │
     ▼                       ▼                       ▼
┌─────────┐           ┌───────────┐           ┌───────────┐
│ MongoDB │           │   Redis   │           │   Kafka   │
└─────────┘           └───────────┘           └───────────┘
     └───────────────────────┴───────────────────────┘
                    docker-compose managed
```

## Troubleshooting

### Infrastructure not starting

```bash
# Check container status
docker-compose ps

# View logs
docker-compose logs -f mongodb
docker-compose logs -f kafka

# Restart a specific service
docker-compose restart mongodb
```

### Connection refused errors

Ensure docker-compose services are healthy before starting AppHost:

```bash
# Wait for all services
docker-compose up -d --wait
```

### Clean restart

```bash
# Stop and remove everything
docker-compose down -v

# Start fresh
docker-compose up -d
```
