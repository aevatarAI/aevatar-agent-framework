# Scientific Research Assistant

A specialized AI Agent built with **Aevatar** framework, powered by **Claude Scientific Skills**.

## Overview
This assistant integrates with the [Claude Scientific Skills](https://github.com/K-Dense-AI/claude-scientific-skills) via the Model Context Protocol (MCP). It provides access to a wide range of scientific tools including:
- **Bioinformatics**: BioPython, Scanpy, etc.
- **Cheminformatics**: RDKit, PubChem, etc.
- **Literature Search**: PubMed, OpenAlex.
- **Data Analysis**: Pandas, Scikit-learn.

## Prerequisites
- .NET 10.0 SDK
- An OpenAI API Key (or other supported LLM provider)
- Internet access (to connect to the hosted MCP server at `mcp.k-dense.ai`)

## Configuration
1. Open `appsettings.json`.
2. Add your OpenAI API Key:
   ```json
   "LLMProviders": {
     "Providers": [
       {
         "Name": "default",
         "ProviderType": "OpenAI",
         "Configuration": {
           "ApiKey": "sk-..." 
         }
       }
     ]
   }
   ```
   *Alternatively, set the `OPENAI_API_KEY` environment variable.*

## Running the Assistant
```bash
dotnet run
```

## Usage
Once running, you can ask questions like:
- "Search PubMed for the latest papers on CRISPR-Cas9 off-target effects."
- "Get the molecular weight of Aspirin using RDKit."
- "Analyze this protein sequence: MKTVRQERLKSIVRILERSKEPVSGAQLAEELSVSRQVIVQDIAYLRSLGYNIVATPRGYVLAGG."

## Architecture
- **ResearchAgent**: The core agent logic that registers the MCP tools.
- **MCP Client**: Connects to the remote K-Dense MCP server via HTTP.
- **Aevatar Framework**: Handles the agent lifecycle, state, and LLM orchestration.
