# C-UoT Creative Reasoning System Demo

A demonstration of **Universe of Thoughts (UoT)** creative reasoning using the Aevatar Agent Framework.

Based on the research paper: [Universe of Thoughts: Enabling Creative Reasoning with Large Language Models](https://arxiv.org/html/2511.20471v2)

## What is C-UoT?

**Combinational UoT (C-UoT)** is a creative reasoning framework that generates novel solutions by:

1. **Analogical Retrieval**: Finding structurally similar problems from different domains
2. **Thought Decomposition**: Breaking solutions into atomic, recombinable thought units
3. **Host Selection**: Choosing the best base solution for modification
4. **Donor Selection**: Using Far-then-Analogical method to prioritize novel ideas
5. **Synthesis**: Combining host and donor thoughts into new solutions
6. **Evaluation**: Three-dimensional scoring (Feasibility, Utility, Novelty)

## Quick Start

### 1. Configure API Key

Edit `appsettings.secrets.json`:

```json
{
  "LLMProviders": {
    "Providers": {
      "deepseek": {
        "ApiKey": "your-actual-api-key"
      }
    }
  }
}
```

### 2. Run the Demo

```bash
cd examples/CreativeSystem
dotnet run
```

### 2.1 ExecutionTrace Bundles (Default Output)

By default, the framework exports `ExecutionTrace` bundles to `<repoRoot>/trace` (best-effort).

If you want to override the output directory, set:

```bash
export AEVATAR_TRACE_DIR="/abs/path/to/aevatar_traces"
```

### 3. Open Browser

Navigate to `http://localhost:5000` to access the creative reasoning console.

## Sample Problems

The demo includes several pre-configured creative problems:

| Problem | Category | Description |
|---------|----------|-------------|
| 🏗️ Bridge Traffic | Engineering | Design two-way traffic management for a single-lane bridge |
| 💼 Bookstore Revival | Business | Help a traditional bookstore compete with e-commerce |
| 📱 Senior Fitness App | Product | Create engaging fitness features for users 60+ |
| 💼 Innovative Drink | Business | Develop a unique beverage product concept |
| 🏢 Remote Collaboration | Workplace | Solve "Zoom fatigue" while maintaining productivity |
| 🌱 Urban Sustainability | Environment | Reduce food waste in urban residential buildings |

## Evaluation Dimensions

| Dimension | Role | Description |
|-----------|------|-------------|
| **Feasibility** | Hard Constraint | Can this solution be implemented? (Must pass threshold) |
| **Utility** | Soft Metric | How effective is this solution? |
| **Novelty** | Soft Metric | How different from existing approaches? |

## Architecture

```
CreativeSystem/
├── Program.cs                 # ASP.NET Core app setup
├── Infrastructure/
│   └── CreativeProjectService.cs  # C-UoT execution service
├── wwwroot/
│   ├── index.html             # UI
│   ├── styles.css             # Cosmic theme
│   └── app.js                 # Frontend logic
└── appsettings.json           # Configuration
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/problems` | GET | List sample problems |
| `/api/solve` | POST | Start creative reasoning |
| `/api/runs/{id}/status` | GET | Get execution status |
| `/api/runs/{id}/result` | GET | Get final result |
| `/api/runs/{id}/events` | GET (SSE) | Real-time progress events |

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `maxAnalogies` | 5 | Maximum analogous problems to find |
| `maxCandidates` | 10 | Maximum candidate solutions to generate |
| `feasibilityThreshold` | 0.6 | Minimum feasibility score (0-1) |
| `utilityWeight` | 0.5 | Weight for utility in composite score |
| `noveltyWeight` | 0.5 | Weight for novelty in composite score |

## Future Extensions

- **E-UoT (Exploratory)**: Discover new thoughts beyond existing solution space
- **T-UoT (Transformative)**: Break rules and assumptions for radical innovation

## References

- [Universe of Thoughts Paper (arXiv)](https://arxiv.org/html/2511.20471v2)
- [Aevatar Agent Framework](../../README.md)

