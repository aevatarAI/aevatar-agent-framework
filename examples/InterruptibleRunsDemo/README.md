# InterruptibleRunsDemo

Console-only demo showing **interruptible runs (latest-wins)** on **Local runtime**.

What you should see:
- Start `runA`: prints `runA:tick:0`, `runA:tick:1`, ...
- Start `runB`: immediately prints `runB:tick:0`
- `runA` stops printing further ticks and its RPC call returns **canceled** (success=false)

## Run

```bash
cd examples/InterruptibleRunsDemo
dotnet run
```

Notes:
- No servers are started, no ports are used (so it will not bind `:5000`).
- This demo uses the framework's RPC run binding (`run_id` metadata) to trigger cancellation.


