# Aevatar Agent Framework - Observability Guide

Complete observability support for monitoring agent operations via **Aspire Dashboard** and OpenTelemetry.

## Overview

The framework provides three specialized telemetry sources:

| Source | Meter | Purpose |
|--------|-------|---------|
| `Aevatar.Agents` | `Aevatar.Agents` | Agent lifecycle, event handling, hierarchy |
| `Aevatar.LLM` | `Aevatar.LLM` | LLM calls, tokens, streaming |
| `Aevatar.Workflow` | `Aevatar.Workflow` | Workflow execution, steps, voting |

## Quick Start

### 1. Add Telemetry to Your App

```csharp
// Aspire integration (auto-detects environment)
builder.Services.AddAevatarAspireTelemetry("my-agent-service");

// Or manual OTLP configuration
builder.Services.AddAevatarTelemetryOtlpExporter(
    serviceName: "my-agent-service",
    otlpEndpoint: "http://localhost:4317");
```

### 2. View in Aspire Dashboard

Start Aspire Dashboard:
```bash
docker run -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Access at: http://localhost:18888

## Metrics Reference

### Agent Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `aevatar.agent.activations.total` | Counter | Total agent activations |
| `aevatar.agent.deactivations.total` | Counter | Total agent deactivations |
| `aevatar.agents.active` | Gauge | Currently active agents |
| `aevatar.events.received.total` | Counter | Events received |
| `aevatar.events.handled.total` | Counter | Events successfully handled |
| `aevatar.events.published.total` | Counter | Events published |
| `aevatar.events.routed.total` | Counter | Events routed through hierarchy |
| `aevatar.events.dropped.total` | Counter | Events with no handlers |
| `aevatar.handlers.invoked.total` | Counter | Handler invocations |
| `aevatar.handlers.errors.total` | Counter | Handler errors |
| `aevatar.event.handling.duration` | Histogram | Event handling duration (ms) |
| `aevatar.handler.execution.duration` | Histogram | Single handler duration (ms) |
| `aevatar.event.publish.duration` | Histogram | Event publish duration (ms) |
| `aevatar.event.routing.duration` | Histogram | Event routing duration (ms) |

### LLM Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `aevatar.llm.calls.total` | Counter | Total LLM API calls |
| `aevatar.llm.calls.success.total` | Counter | Successful calls |
| `aevatar.llm.calls.errors.total` | Counter | Failed calls |
| `aevatar.llm.tokens.prompt.total` | Counter | Prompt tokens consumed |
| `aevatar.llm.tokens.completion.total` | Counter | Completion tokens generated |
| `aevatar.llm.tokens.total` | Counter | Total tokens |
| `aevatar.llm.call.duration` | Histogram | Call duration (ms) |
| `aevatar.llm.ttft` | Histogram | Time to first token (ms) |
| `aevatar.llm.tokens.per_second` | Histogram | Token generation rate |
| `aevatar.llm.calls.active` | Gauge | Active LLM calls |
| `aevatar.llm.streaming.active` | Gauge | Active streaming sessions |
| `aevatar.llm.streaming.chunks.total` | Counter | Streaming chunks received |
| `aevatar.llm.embeddings.calls.total` | Counter | Embedding API calls |
| `aevatar.llm.embeddings.tokens.total` | Counter | Embedding tokens |
| `aevatar.llm.tools.calls.total` | Counter | Tool/function calls |
| `aevatar.llm.retries.total` | Counter | Retry attempts |

### Workflow Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `aevatar.workflow.started.total` | Counter | Workflows started |
| `aevatar.workflow.completed.total` | Counter | Workflows completed |
| `aevatar.workflow.failed.total` | Counter | Workflows failed |
| `aevatar.workflow.active` | Gauge | Active workflows |
| `aevatar.workflow.duration` | Histogram | Workflow duration (ms) |
| `aevatar.workflow.steps.executed.total` | Counter | Steps executed |
| `aevatar.workflow.steps.failed.total` | Counter | Steps failed |
| `aevatar.workflow.step.duration` | Histogram | Step duration (ms) |
| `aevatar.workflow.parallel.dispatched.total` | Counter | Parallel tasks dispatched |
| `aevatar.workflow.parallel.completed.total` | Counter | Parallel tasks completed |
| `aevatar.workflow.parallel.active` | Gauge | Active parallel tasks |
| `aevatar.workflow.fanout.duration` | Histogram | Fan-out duration (ms) |
| `aevatar.workflow.fanout.size` | Histogram | Tasks per fan-out |
| `aevatar.workflow.vote.rounds.total` | Counter | Voting rounds |
| `aevatar.workflow.vote.consensus.total` | Counter | Consensus reached |
| `aevatar.workflow.vote.active` | Gauge | Active voting sessions |
| `aevatar.workflow.vote.duration` | Histogram | Vote duration (ms) |
| `aevatar.workflow.vote.rounds_to_consensus` | Histogram | Rounds to consensus |
| `aevatar.workflow.redflags.total` | Counter | Red flags detected |
| `aevatar.workflow.tokens.total` | Counter | Workflow tokens used |
| `aevatar.workflow.llm_calls.total` | Counter | Workflow LLM calls |

## Traces Reference

### Agent Traces

| Activity Name | Kind | Description |
|--------------|------|-------------|
| `agent.activate` | Internal | Agent activation |
| `agent.deactivate` | Internal | Agent deactivation |
| `agent.event.handle` | Consumer | Event handling pipeline |
| `agent.handler.execute` | Internal | Single handler execution |
| `agent.event.publish` | Producer | Event publishing |
| `agent.event.route` | Internal | Event routing |
| `agent.hierarchy.set_parent` | Internal | Parent relationship |
| `agent.hierarchy.add_child` | Internal | Child relationship |
| `agent.state.load` | Client | State loading |
| `agent.state.save` | Client | State saving |

### LLM Traces

| Activity Name | Kind | Description |
|--------------|------|-------------|
| `llm.call` | Client | LLM API call |
| `llm.embedding` | Client | Embedding API call |
| `llm.tool.call` | Internal | Tool/function call |

### Workflow Traces

| Activity Name | Kind | Description |
|--------------|------|-------------|
| `workflow.execute` | Internal | Workflow execution |
| `workflow.step` | Internal | Step execution |
| `workflow.fanout` | Internal | Fan-out parallel execution |
| `workflow.vote` | Internal | Voting step |
| `workflow.worker` | Consumer | Worker task |

## Trace Attributes

### Common Attributes

```
agent.id          - Agent unique identifier
agent.type        - Agent class name
event.id          - Event unique identifier
event.type        - Event type name
correlation.id    - Request correlation ID
duration.ms       - Operation duration
```

### LLM Attributes

```
llm.provider      - Provider name (openai, azure, etc.)
llm.model         - Model identifier
llm.streaming     - Is streaming mode
llm.tokens.prompt     - Prompt tokens
llm.tokens.completion - Completion tokens
llm.tokens.total      - Total tokens
llm.ttft.ms           - Time to first token
llm.error.type        - Error type on failure
```

### Workflow Attributes

```
workflow.execution_id  - Execution identifier
workflow.name          - Workflow name
step.id                - Step identifier
step.type              - Step type (llm_call, fan_out, vote, etc.)
step.depth             - Recursion depth
fanout.task_count      - Number of parallel tasks
vote.required          - Required votes for consensus
vote.rounds_used       - Actual rounds used
```

## Structured Logging

The framework uses high-performance structured logging with consistent scopes:

```csharp
// Agent operations automatically include:
{
    "AgentId": "...",
    "AgentType": "MyAgent",
    "Operation": "EventHandling",
    "Phase": "Processing",
    "EventId": "...",
    "EventType": "MyEvent"
}

// LLM calls include:
{
    "AgentId": "...",
    "LLMProvider": "openai",
    "LLMModel": "gpt-4",
    "Operation": "LLMCall",
    "Phase": "AI"
}

// Workflow execution includes:
{
    "ExecutionId": "...",
    "WorkflowName": "my-workflow",
    "StepId": "analyze",
    "StepType": "llm_call",
    "Operation": "StepExecution",
    "Phase": "Step"
}
```

## Integration Examples

### Aspire Dashboard Configuration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Aevatar services
builder.Services.AddAevatar()
    .AddLocalRuntime();

// Add Telemetry (auto-detects Aspire)
builder.Services.AddAevatarAspireTelemetry();

// Or explicit OTLP
builder.Services.AddAevatarTelemetryOtlpExporter(
    serviceName: "paper-review-agent",
    otlpEndpoint: Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

var app = builder.Build();
```

### Custom Metrics Filtering

```csharp
builder.Services.AddAevatarTelemetry(
    serviceName: "my-service",
    configureMetrics: metrics =>
    {
        // Add custom views
        metrics.AddView(
            instrumentName: "aevatar.llm.call.duration",
            new ExplicitBucketHistogramConfiguration
            {
                Boundaries = new[] { 100, 500, 1000, 2000, 5000 }
            });
    });
```

### Prometheus Export

```csharp
builder.Services.AddAevatarTelemetry(
    configureMetrics: metrics =>
    {
        metrics.AddPrometheusExporter();
    });

app.MapPrometheusScrapingEndpoint();
```

## Dashboard Screenshots

### Metrics Dashboard
- Active agents over time
- Event throughput
- LLM token consumption
- Workflow success rate

### Trace Explorer
- End-to-end request tracing
- Agent hierarchy visualization
- LLM call breakdown
- Workflow step timeline

### Structured Logs
- Filtered by correlation ID
- Agent operation context
- Error tracking

## Performance Considerations

1. **Sampling**: For high-volume systems, configure trace sampling:
   ```csharp
   builder.Services.AddAevatarTelemetry(
       configureTracing: tracing =>
       {
           tracing.SetSampler(new TraceIdRatioBasedSampler(0.1)); // 10%
       });
   ```

2. **Metric Cardinality**: Avoid high-cardinality labels (e.g., agent IDs) in production queries.

3. **Log Levels**: Use appropriate log levels:
   - `Debug`: Handler invocations, routing details
   - `Information`: Activations, completions
   - `Warning`: Dropped events, retries
   - `Error`: Failures, exceptions

## Troubleshooting

### No Data in Dashboard
1. Verify OTLP endpoint is accessible
2. Check `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable
3. Ensure OpenTelemetry packages are installed

### Missing Traces
1. Verify activity sources are registered
2. Check sampling configuration
3. Ensure instrumentation is not filtered

### High Memory Usage
1. Reduce metric cardinality
2. Configure batching for exporters
3. Enable trace sampling

## Related Documentation

- [Agent Communication](./AGENT_COMMUNICATION.md)
- [Cognitive Workflows](../src/Aevatar.Agents.Cognitive/README.md)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/net/)
- [Aspire Dashboard](https://learn.microsoft.com/aspire/fundamentals/dashboard/overview)
