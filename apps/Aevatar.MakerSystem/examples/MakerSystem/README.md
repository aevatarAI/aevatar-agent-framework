# MAKER Projects Demo

A clean, minimal demo showcasing MAKER's capabilities.

## Running

```bash
cd apps/Aevatar.MakerSystem/examples/MakerSystem
dotnet run
```

Then open http://localhost:5088 in your browser.

## Configuration

Create `appsettings.secrets.json` with your API keys:

```json
{
  "LLMProviders": {
    "providers": {
      "deepseek": {
        "api_key": "your-deepseek-api-key"
      }
    }
  }
}
```

Or set environment variables:
```bash
export DEEPSEEK_API_KEY=your-key
```

## Trace Bundle 输出（默认启用）

默认情况下，框架会把每次执行的 `ExecutionTrace` 输出到 `<repoRoot>/trace`（best-effort）。

如果你希望把输出目录改到其他位置（覆盖默认），请设置：

```bash
export AEVATAR_TRACE_DIR="/abs/path/to/aevatar_traces"
```

输出目录结构（Trace Bundle v1）：

```
${AEVATAR_TRACE_DIR}/<executionId>/
  trace.pb
  trace.json
  manifest.json
  artifacts/
```

## Projects

### 八字推演 (Bazi Analysis)
Multi-agent Bazi (Chinese Astrology) analysis demonstrating:
- Domain-specific decomposition strategy
- Context propagation between steps
- Structured final report generation

### 论文总结 (Paper Summary)
Multi-agent paper summarization demonstrating:
- Section-based decomposition
- Content injection into prompts
- Markdown report synthesis

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   Program.cs                         │
│  - Register services                                 │
│  - Map API endpoints                                 │
└───────────────────────────┬─────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────┐
│              MakerProjectService                     │
│  - GetProjects()                                     │
│  - StartRunAsync()  →  IMakerExecutor.ExecuteAsync() │
│  - GetStatus/Snapshot/Timeline()                     │
└───────────────────────────┬─────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────┐
│                IMakerExecutor                        │
│  (from Aevatar.Agents.Maker)                     │
│  - Handles decomposition, voting, composition       │
│  - Uses strategies from Projects/Bazi or Paper      │
└─────────────────────────────────────────────────────┘
```

## Key Insight

The user code is now **purely domain-focused**:
- `BaziDecomposer`: How to break down Bazi analysis
- `BaziSolver`: How to solve atomic Bazi steps
- `PaperDecomposer`: How to break down paper summary
- `PaperSolver`: How to summarize paper sections

All framework complexity (voting, error correction, recursion) is handled by MAKER.

