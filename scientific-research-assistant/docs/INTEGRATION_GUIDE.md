# Integration Guide: Claude Scientific Skills

This guide explains how to integrate and customize the `claude-scientific-skills` repository with your Research Assistant.

**Repository**: [https://github.com/K-Dense-AI/claude-scientific-skills](https://github.com/K-Dense-AI/claude-scientific-skills)

## 1. Overview
The Aevatar Framework connects to these skills using the **Model Context Protocol (MCP)**. This means the Python code in the repository runs in a separate process (or server), and the Agent communicates with it via a standardized protocol. You do **not** need to manually copy Python files into C#.

## 2. Integration Methods

### Option A: Hosted Server (Zero Setup)
By default, the agent is configured to use the hosted MCP server provided by K-Dense AI.
- **Config**: `Type: "Http"`
- **URL**: `https://mcp.k-dense.ai/claude-scientific-skills/mcp`
- **Pros**: Instant start, no installation.
- **Cons**: Data leaves your network.

### Option B: Local Docker (Recommended for Privacy)
Run the exact environment from the repo locally using Docker.
1.  **Requirement**: Docker Desktop installed.
2.  **Config**: Update `appsettings.json`:
    ```json
    "MCP": {
      "Type": "Docker",
      "DockerImage": "ghcr.io/k-dense-ai/claude-scientific-skills:latest"
    }
    ```
3.  **How it works**: The Agent will automatically spin up the Docker container when it starts and communicate via Stdio/Http.

### Option C: Custom Python Integration (Advanced)
If you want to modify the skills or add your own (e.g., from `tree/main/scientific-skills`):

1.  **Clone the Repo**:
    ```bash
    git clone https://github.com/K-Dense-AI/claude-scientific-skills.git
    cd claude-scientific-skills
    ```

2.  **Modify Python Code**:
    Edit the scripts in `scientific-skills/` as needed.

3.  **Run Locally**:
    Ensure you have `uv` or `pip` installed.
    ```bash
    # Run the MCP server manually
    uv run mcp-server-scientific-skills
    ```

4.  **Connect Agent**:
    Change `appsettings.json` to use `Stdio` transport pointing to your local python script:
    *(Requires minor code change in `ResearchAgent.cs` to support generic Stdio path if not fully implemented, or use the Docker method with a locally built image)*.

    **Easiest Path for Custom Code**:
    1. Modify code.
    2. Build local Docker image: `docker build -t my-scientific-skills .`
    3. Update `appsettings.json` `DockerImage`: `my-scientific-skills`.

## 3. Supported Skills
The integration currently supports:
- **Bioinformatics**: BLAST, PDB search.
- **Cheminformatics**: PubChem search, RDKit.
- **Calculators**: Unit conversion, statistical analysis.
- **Literature**: PubMed and ArXiv search.

## 4. Troubleshooting
- **Connection Refused**: Ensure the MCP server is running or Docker is accessible.
- **Tool Timeout**: Complex scientific queries can take time. Increase `"RequestTimeoutMs"` in `appsettings.json`.
