# Aevatar.Agents.A2UI

A2UI (Agent Adaptive UI) is a protocol for Agents to drive frontend user interfaces dynamically.

## Purpose

This library provides the Protocol Buffers definitions and C# event records for the A2UI protocol, enabling agents to:
- Render dynamic surfaces (Adaptive Cards, HTML, etc.)
- Update data models in real-time
- Handle user interactions from the UI

## Architecture

- **Protos/**: Contains `a2ui.proto`, defining the wire format for inter-agent and agent-sidecar communication.
- **Events/**: Contains C# records (mirroring `AgUiEvents`) for easy consumption by frontend SDKs (via SignalR/SSE).

## Usage

Agents can publish A2UI events using the Protobuf messages defined in `Aevatar.Agents.A2UI.Protobuf`.

```csharp
await PublishAsync(new SurfaceUpdate 
{
    SurfaceId = "main_dashboard",
    Template = "{ ... json ... }",
    InitialData = ...
});
```

