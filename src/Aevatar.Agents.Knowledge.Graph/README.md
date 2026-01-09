# Aevatar.Agents.Knowledge.Graph

A session-scoped knowledge graph library designed for scientific research assistants. It enables building, querying, and exporting interconnected knowledge as structured inference chains and research papers.

## Purpose

This library provides a directed acyclic graph (DAG) structure for managing scientific knowledge with:

- **Knowledge Nodes**: Represent pieces of scientific knowledge (axioms, theorems, experiments, definitions, etc.)
- **Inference Edges**: Connect nodes via dependency relationships (A depends on B means A is derived from B)
- **Session Isolation**: Each session maintains its own isolated knowledge graph
- **Paper Generation**: Automatically generate mini research papers from knowledge chains

Typical use cases:
- AI research assistants that need to track reasoning chains
- Scientific knowledge management systems
- Educational tools that explain derivation of concepts
- Any application requiring traceable knowledge dependencies

## Design Philosophy

### 1. Session-Scoped Isolation

Each knowledge graph client operates within a session scope. This enables:
- Multi-tenant scenarios where different users/sessions have independent graphs
- Clean separation of knowledge contexts
- Safe concurrent access patterns

```csharp
var factory = serviceProvider.GetRequiredService<IKnowledgeGraphClientFactory>();
var client = factory.CreateClient("session-123"); // All operations scoped to this session
```

### 2. DAG Structure for Valid Inference Chains

Knowledge graphs must remain acyclic to maintain valid logical inference chains:
- A node can depend on multiple existing nodes (multiple premises)
- Circular dependencies are automatically detected and rejected
- This ensures all knowledge can be traced back to foundational axioms

### 3. Pluggable Storage Backend

The library uses `IGraphClient` from `Aevatar.Agents.Persistence.Graph` as its storage backend:
- **In-Memory**: Fast, ephemeral storage for testing or transient sessions
- **Neo4j**: Persistent graph database for production use

### 4. Optional Resource Storage

Knowledge nodes can have associated files (datasets, proofs, figures):
- Files are automatically zipped and uploaded to S3-compatible storage
- Presigned URLs are generated for secure download access
- Falls back to local file paths when S3 is not configured

## Architecture

```
Aevatar.Agents.Knowledge.Graph/
├── IKnowledgeGraphClient.cs       # Main public API interface
├── KnowledgeGraphClient.cs        # Implementation + Factory
├── Models/
│   ├── KnowledgeNode.cs           # Node data model
│   ├── KnowledgeEdge.cs           # Edge data model
│   ├── KnowledgeNodeType.cs       # Node type enumeration
│   ├── KnowledgeSnapshot.cs           # Complete graph state
│   ├── KnowledgeChain.cs          # Inference chain structure
│   └── KnowledgeChainDetails.cs   # Chain + Markdown description
├── Exceptions/
│   ├── NodeNotFoundException.cs   # Node doesn't exist
│   ├── DuplicateNodeException.cs  # Node ID already exists
│   └── CycleDetectedException.cs  # Would create a cycle
├── Storage/
│   ├── IFileStorage.cs            # File storage abstraction
│   ├── NullFileStorage.cs         # No-op implementation
│   └── S3FileStorage.cs           # S3-compatible storage
├── Store/
│   ├── IKnowledgeGraphStore.cs    # Internal storage interface
│   └── GraphClientBackedStore.cs  # IGraphClient implementation
├── Validation/
│   └── DagValidator.cs            # Cycle detection
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs  # DI registration
```

## Installation

Add a reference to the project:

```xml
<ProjectReference Include="path/to/Aevatar.Agents.Knowledge.Graph.csproj" />
```

## Usage Examples

### Basic Setup with Dependency Injection

```csharp
// Program.cs or Startup.cs
services.AddAevatarGraphInMemory();  // Or AddAevatarGraphNeo4j(...)
services.AddKnowledgeGraph();
```

### Creating a Session-Scoped Client

```csharp
var factory = serviceProvider.GetRequiredService<IKnowledgeGraphClientFactory>();
var client = factory.CreateClient("research-session-001");
```

### Adding Knowledge Nodes

```csharp
// Add a foundational axiom (no dependencies)
var axiom = await client.AddNodeAsync(
    nodeId: "empty-set",
    nodeType: KnowledgeNodeType.MathAxiom,
    coreDescription: "The empty set exists",
    detailedDescription: "There exists a set with no elements, denoted as {} or ∅.");

// Add a definition that depends on the axiom
var definition = await client.AddNodeAsync(
    nodeId: "natural-zero",
    nodeType: KnowledgeNodeType.MathDefinition,
    coreDescription: "Zero is defined as the empty set",
    detailedDescription: "In von Neumann ordinals, 0 := ∅.",
    dependsOn: ["empty-set"]);

// Add a theorem with proof and resource files
var theorem = await client.AddNodeAsync(
    nodeId: "successor-function",
    nodeType: KnowledgeNodeType.MathTheorem,
    coreDescription: "The successor function is well-defined",
    detailedDescription: "For any natural number n, S(n) = n ∪ {n} is also a natural number.",
    proof: "By induction on n...",
    resourceFolderPath: "/path/to/proof-diagrams",  // Will be zipped and uploaded to S3
    dependsOn: ["natural-zero"]);
```

### Getting Knowledge Chain Details

```csharp
// Get the full derivation chain and description for a node
var details = await client.GetKnowledgeChainDetailsAsync("successor-function");

// Access the structured chain
var chain = details.Chain;
Console.WriteLine($"Target: {chain.TargetNode.CoreDescription}");
Console.WriteLine($"Chain depth: {chain.MaxDepth}");
Console.WriteLine($"Total nodes: {chain.TotalNodes}");

// Iterate through levels (Level 0 = target, Level N = foundations)
foreach (var level in chain.Levels)
{
    Console.WriteLine($"Level {level.Depth}:");
    foreach (var node in level.Nodes)
    {
        Console.WriteLine($"  - {node.CoreDescription}");
    }
}

// Access the Markdown description (mini research paper)
string markdownDescription = details.Description;
// Contains:
// - Title and abstract
// - Derivation path from foundations to target
// - Full details of each node (ID, Type, CoreDescription, DetailedDescription, Proof, ResourceUri)
// - References section
```

### Generating Full Research Paper

```csharp
// Generate a comprehensive paper from entire graph
string fullPaper = await client.GenerateFullPaperAsync();
// Returns Markdown with all nodes organized by type and inference relationships
```

### Working with Graph Snapshots

```csharp
// Get complete graph state
var snapshot = await client.GetKnowledgeSnapshotAsync();

Console.WriteLine($"Session: {snapshot.SessionId}");
Console.WriteLine($"Nodes: {snapshot.NodeCount}");
Console.WriteLine($"Edges: {snapshot.EdgeCount}");

// Access all nodes and edges
foreach (var node in snapshot.Nodes)
{
    Console.WriteLine($"{node.Id}: {node.CoreDescription}");
}
```

### Removing Nodes

```csharp
// Removes the node and all connected edges
// Also deletes associated S3 files
bool removed = await client.RemoveNodeAsync("successor-function");
```

### S3 Storage Configuration

```csharp
services.AddAevatarGraphNeo4j(options =>
{
    options.Uri = "bolt://localhost:7687";
    options.User = "neo4j";
    options.Password = "password";
});

services.AddKnowledgeGraph(options =>
{
    // MinIO (local development)
    options.Endpoint = "localhost:9000";
    options.AccessKey = "minioadmin";
    options.SecretKey = "minioadmin";
    options.BucketName = "knowledge-graph";
    options.UseSsl = false;

    // AWS S3
    // options.Endpoint = "s3.us-east-1.amazonaws.com";
    // options.Region = "us-east-1";
    // options.AccessKey = "AKIA...";
    // options.SecretKey = "...";
    // options.BucketName = "my-knowledge-bucket";
    // options.UseSsl = true;
});
```

## Knowledge Node Types

The library provides predefined node types for various scientific domains:

| Category | Types |
|----------|-------|
| Mathematics | `MathAxiom`, `MathTheorem`, `MathProof`, `MathDefinition`, `MathLemma`, `MathCorollary` |
| Physics | `PhysicsLaw`, `PhysicsTheory`, `PhysicsExperiment` |
| Biology | `BiologyExperiment`, `BiologyProcess`, `BiologyStructure` |
| Chemistry | `ChemistryExperiment`, `ChemistryReaction` |
| Computer Science | `CsAlgorithm`, `CsDataStructure`, `CsDesignPattern` |
| Research | `ResearchPaper`, `ResearchDataset`, `ResearchAnalysis`, `ResearchHypothesis` |
| Documentation | `Note`, `Summary`, `Reference` |

## Exception Handling

```csharp
try
{
    await client.AddNodeAsync("theorem-1", KnowledgeNodeType.MathTheorem,
        "A theorem", "Details...",
        dependsOn: ["non-existent-axiom"]);
}
catch (NodeNotFoundException ex)
{
    Console.WriteLine($"Dependency not found: {ex.NodeId}");
}
catch (DuplicateNodeException ex)
{
    Console.WriteLine($"Node already exists: {ex.NodeId}");
}
catch (CycleDetectedException ex)
{
    Console.WriteLine($"Would create cycle: {ex.FromNodeId} -> {ex.ToNodeId}");
}
```

## Thread Safety

- `IKnowledgeGraphClient` instances are designed for single-session use
- The underlying `IGraphClient` implementations handle their own thread safety
- For concurrent access to the same session, consider using appropriate synchronization

## Dependencies

- `Aevatar.Agents.Persistence.Graph` - Graph storage abstraction (IGraphClient)
- `Microsoft.Extensions.DependencyInjection.Abstractions` - DI support
- `Microsoft.Extensions.Options` - Options pattern
- `Minio` - S3-compatible storage client (optional)

## License

See the root project license.
