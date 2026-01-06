# Performance Tests for Orleans Stream

This benchmark suite tests the performance of Orleans Stream with the Aevatar Agent Framework.

## Prerequisites

- MongoDB running on `localhost:27017`
- Orleans Silo running on `localhost:30000`
- HttpApi.Host running on `https://localhost:44345` (see `apps/Aevatar.App/src/Aevatar.App.HttpApi.Host/Properties/launchSettings.json`)

## Running Tests

### Standard Performance Tests

```bash
cd src/Aevatar.App
./benchmarks/run-perf.sh
```

Or manually:
```bash
cd benchmarks/PerformanceTests
dotnet run
```

### Multi-Topic Multi-Agent Benchmark

**New!** Test Orleans Stream behavior with multiple agent types and topics:

```bash
cd benchmarks/PerformanceTests
dotnet run multitopic
```

## Test Scenarios

### Standard Tests

1. **Agent Creation Time** - Measures time to create and register an agent
2. **Message Publishing** - Tests throughput of publishing multiple messages
3. **Agent Info Query Time** - Tests state query performance
4. **Parent-Child Event Propagation** - Measures end-to-end propagation time

### Multi-Topic Benchmark

**Purpose**: Test topic isolation and message distribution across multiple agent types.

**Scenario**:
- 5 different agent types (Type A, B, C, D, E)
- 100 agents per type (total 500 agents)
- Publish 50 messages to Type A agents only

**What it tests**:
1. **Topic Isolation**: Do other types (B,C,D,E) receive messages? (Should NOT)
2. **Distribution Pattern**: How do the 100 Type A agents receive messages?
3. **Bottleneck Analysis**: Is this Orleans Stream limitation or Queue limitation?

**Expected Results**:
- ✅ Only Type A agents receive messages (topic isolation works)
- ✅ All 100 Type A agents receive messages (no dead agents)
- ✅ Even distribution (~50 messages per agent)
- 📊 Low variance indicates good stream distribution

**Output Example**:
```
🎯 Multi-Topic Multi-Agent Benchmark
============================================================
Configuration:
  • Agent Types: 5 (Type A, B, C, D, E)
  • Agents per Type: 100
  • Total Agents: 500
  • Test Messages: 50

📦 Step 1: Creating agents...
   ✅ Created 500 agents in 15000ms

📡 Step 2: Creating publisher agent...
   ✅ Publisher created

🔗 Step 3: Setting up subscriptions (Type A agents → Publisher)...
   ✅ Subscriptions established in 2500ms

📤 Step 4: Publishing 50 messages to Type A agents...
   ✅ Published in 250ms

📊 Step 5: Collecting statistics...

============================================================
📊 Test Results Summary
============================================================

🎯 TypeA (100 agents):
   • Agents Received: 100/100 (100.0%)
   • Total Messages: 5000
   • Min/Max/Avg/Median: 48/52/50.0/50.0
   • Distribution histogram:
      48 msgs: ████ (5 agents)
      49 msgs: ████████ (12 agents)
      50 msgs: ████████████████████████████████ (66 agents)
      51 msgs: ████████ (12 agents)
      52 msgs: ████ (5 agents)

🚫 TypeB (100 agents):
   • Agents Received: 0/100 (0.0%)
   • Total Messages: 0
   • ✅ No messages received (as expected)

🚫 TypeC, TypeD, TypeE: Similar to TypeB

🔍 Analysis:
✅ Topic Isolation: Verified (only Type A received messages)
✅ All Type A agents received messages
📈 Distribution Pattern:
   • Expected per agent: 50 messages
   • Actual average: 50.0 messages
   • Distribution quality: 100.0%
   ✅ Excellent distribution (near perfect)

🔬 Bottleneck Analysis:
   • Message variance: 1.2
   • Pattern: Even distribution (Orleans Stream working well)
```

## Metrics

| Metric | Standard | Multi-Topic (500 agents) | Notes |
|--------|----------|-------------------------|-------|
| Agent Creation | 50-150 ms | 15-30 seconds | Batch creation |
| Message Publish | 5-10 ms | 5-10 ms | Per message |
| Stream Propagation | 50-200 ms | 100-500 ms | End-to-end |
| Throughput | 100-200 msg/s | 200-500 msg/s | Dependent on config |

## Configuration

The benchmark connects to:
- **MongoDB**: `mongodb://localhost:27017/AevatarBusiness`
- **Orleans Cluster**: `localhost:30000`
- **Stream Provider**: Memory Stream (Development) or Kafka (Production)

Configuration is loaded from `appsettings.Development.json` when running in Development environment.

## Interpreting Multi-Topic Results

### Topic Isolation
- **Good**: Only subscribed agents (Type A) receive messages
- **Bad**: Other types receive messages → indicates cross-topic leakage

### Distribution Quality
- **Excellent (>95%)**: Near perfect message distribution
- **Fair (80-95%)**: Some imbalance but acceptable
- **Poor (<80%)**: Significant message loss or imbalance

### Variance Analysis
- **Low (<1.0)**: Even distribution, Orleans Stream working well
- **Medium (1-10)**: Slight variance, acceptable queue behavior
- **High (>10)**: Possible queue/partition bottleneck

### Troubleshooting

If distribution is poor:
1. Check Stream Provider configuration (Memory vs Kafka)
2. Verify partition count in Kafka (if using Kafka)
3. Check Orleans clustering health
4. Monitor network latency
5. Review agent activation times
