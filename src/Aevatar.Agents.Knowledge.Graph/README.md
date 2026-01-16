# Aevatar.Agents.Knowledge.Graph

A session-scoped knowledge graph library designed for scientific research assistants. It enables building, querying, and exporting interconnected knowledge as structured inference chains and research papers.

## Purpose

This library provides a directed acyclic graph (DAG) structure for managing scientific knowledge with:

- **Plan Nodes**: Represent research plan steps with status tracking (Pending/Active/Completed)
- **Knowledge Nodes**: Represent pieces of scientific knowledge (axioms, theorems, experiments, definitions, etc.)
- **Edge Types**: Connect nodes via typed relationships (DependsOn, MotivatedBy, Promotes, CrossSessionReference)
- **Session Isolation**: Each session maintains its own isolated knowledge graph
- **Cross-Session References**: Reference knowledge from other sessions via `IGlobalKnowledgeIndex`
- **Pivot Operations**: Support for research direction changes with node preservation
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

### 3. Node Types: Plan vs Knowledge

The graph supports two primary node types, both implementing `IGraphNode`:

- **PlanNode**: Represents a research plan step with execution status
  - States: Pending → Active → Completed
  - Tracks methodology and progress
  - Can promote goals or other nodes

- **KnowledgeNode**: Represents asserted scientific knowledge
  - Has a specific `KnowledgeNodeType` (MathAxiom, MathTheorem, etc.)
  - Can have attestations (pubkey, signature pairs)
  - Tracks derivation process and references

### 4. Edge Type System

The library uses typed edges via `IGraphEdge`:

| Edge Type | Description |
|-----------|-------------|
| `PlanDependsOnPlan` | Plan step depends on another plan step |
| `KnowledgeDependsOnKnowledge` | Knowledge derived from other knowledge |
| `KnowledgeMotivatedByPlan` | Knowledge produced by executing a plan |
| `PlanPromotesGoal` | Plan promotes achieving a goal |
| `CrossSessionReference` | References knowledge from another session |

### 5. Pluggable Storage Backend

The library uses `IGraphClient` from `Aevatar.Agents.Persistence.Graph` as its storage backend:
- **In-Memory**: Fast, ephemeral storage for testing or transient sessions
- **Neo4j**: Persistent graph database for production use

### 6. Optional Resource Storage

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
│   ├── IGraphNode.cs              # Base interface for all nodes
│   ├── PlanNode.cs                # Plan step node
│   ├── KnowledgeNode.cs           # Knowledge node
│   ├── IGraphEdge.cs              # Base interface for all edges
│   ├── KnowledgeEdge.cs           # Legacy edge (deprecated)
│   ├── KnowledgeNodeType.cs       # Knowledge node type enum
│   ├── PlanNodeStatus.cs          # Plan execution status
│   ├── PivotNodeStatus.cs         # Pivot operation status
│   ├── GraphSnapshot.cs           # Complete graph state
│   ├── KnowledgeChain.cs          # Inference chain structure
│   └── KnowledgeChainDetails.cs   # Chain + Markdown description
├── Services/
│   ├── IGlobalKnowledgeIndex.cs   # Cross-session knowledge search
│   └── GlobalKnowledgeIndex.cs    # Implementation
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

### Creating Plan Nodes

```csharp
// Create a research plan step
var planNode = await client.CreatePlanNodeAsync(
    nodeId: "plan-1",
    coreDescription: "Literature review on quantum computing",
    detailedDescription: "Survey recent papers on quantum error correction",
    methodology: "Search arXiv, filter by date and citations",
    sequentialOrder: 1);

// Update plan status
await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Active, "Starting search...");
await client.UpdatePlanNodeStatusAsync("plan-1", PlanNodeStatus.Completed, "Found 15 relevant papers");
```

### Creating Knowledge Nodes

```csharp
// Add knowledge derived from plan execution
var knowledge = await client.CreateKnowledgeNodeAsync(
    nodeId: "knowledge-1",
    nodeType: KnowledgeNodeType.ResearchAnalysis,
    coreDescription: "Quantum error correction requires redundancy",
    detailedDescription: "Analysis shows that logical qubits need multiple physical qubits...",
    derivationProcess: "Synthesized findings from 15 papers",
    references: ["arXiv:2301.00234", "arXiv:2302.01567"],
    motivatedByPlanNodeId: "plan-1");

// Add a foundational axiom (no dependencies)
var axiom = await client.AddNodeAsync(
    nodeId: "empty-set",
    nodeType: KnowledgeNodeType.MathAxiom,
    coreDescription: "The empty set exists",
    detailedDescription: "There exists a set with no elements, denoted as {} or ∅.");

// Add knowledge that depends on other knowledge
var theorem = await client.AddNodeAsync(
    nodeId: "theorem-1",
    nodeType: KnowledgeNodeType.MathTheorem,
    coreDescription: "The successor function is well-defined",
    detailedDescription: "For any natural number n, S(n) = n ∪ {n} is also a natural number.",
    proof: "By induction on n...",
    dependsOn: ["empty-set", "knowledge-1"]);
```

### Cross-Session Knowledge References

```csharp
// Get the global knowledge index
var globalIndex = serviceProvider.GetRequiredService<IGlobalKnowledgeIndex>();

// Search for knowledge across all sessions
var results = await globalIndex.SearchAsync(
    query: "quantum error correction",
    maxResults: 10,
    excludeSessionId: "current-session");  // Exclude current session

foreach (var result in results)
{
    Console.WriteLine($"Found: {result.GlobalId} - {result.Node.CoreDescription}");
}

// Create a reference to knowledge from another session
var edge = await globalIndex.CreateCrossSessionReferenceAsync(
    fromSessionId: "current-session",
    fromNodeId: "my-analysis",
    toGlobalNodeId: "other-session:their-theorem");  // Format: sessionId:nodeId

// Get a specific node from another session
var node = await globalIndex.GetNodeAsync("other-session:axiom-1");
```

### Getting Knowledge Chain Details

```csharp
// Get the full derivation chain and description for a node
var details = await client.GetKnowledgeChainDetailsAsync("theorem-1");

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
```

### Generating Full Research Paper

```csharp
// Generate a comprehensive paper from entire graph
string fullPaper = await client.GenerateFullPaperAsync();
// Returns Markdown with all nodes organized by type and inference relationships
```

### Working with Graph Snapshots

```csharp
// Get complete graph state (use GetGraphSnapshotAsync, not deprecated GetKnowledgeSnapshotAsync)
var snapshot = await client.GetGraphSnapshotAsync();

Console.WriteLine($"Session: {snapshot.SessionId}");
Console.WriteLine($"Plan Nodes: {snapshot.PlanNodes.Count}");
Console.WriteLine($"Knowledge Nodes: {snapshot.KnowledgeNodes.Count}");
Console.WriteLine($"Edges: {snapshot.EdgeCount}");

// Access all nodes (both types)
foreach (var node in snapshot.AllNodes)
{
    Console.WriteLine($"{node.Id}: {node.CoreDescription}");
}
```

### Node Explanation

```csharp
// Get detailed explanation of a node
var explanation = await client.ExplainNodeAsync("theorem-1");

Console.WriteLine($"Node: {explanation.NodeId}");
Console.WriteLine($"Type: {explanation.NodeType}");
Console.WriteLine(explanation.MarkdownContent);
Console.WriteLine($"Direct Dependencies: {string.Join(", ", explanation.DirectDependencies)}");
Console.WriteLine($"Dependents: {string.Join(", ", explanation.Dependents)}");
```

### Removing Nodes

```csharp
// Removes the node and all connected edges
// Also deletes associated S3 files
bool removed = await client.RemoveNodeAsync("theorem-1");
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

## Edge Types

| Type | From | To | Description |
|------|------|-----|-------------|
| `PlanDependsOnPlan` | PlanNode | PlanNode | Execution order dependency |
| `KnowledgeDependsOnKnowledge` | KnowledgeNode | KnowledgeNode | Inference/derivation relationship |
| `KnowledgeMotivatedByPlan` | KnowledgeNode | PlanNode | Knowledge produced by plan execution |
| `PlanPromotesGoal` | PlanNode | Any | Plan promotes achieving a goal |
| `CrossSessionReference` | KnowledgeNode | KnowledgeNode (other session) | Cross-session citation |

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

## Migration Notes

### From KnowledgeSnapshot to GraphSnapshot

The `GetKnowledgeSnapshotAsync` method is deprecated. Use `GetGraphSnapshotAsync` instead:

```csharp
// Old (deprecated)
var snapshot = await client.GetKnowledgeSnapshotAsync();

// New
var snapshot = await client.GetGraphSnapshotAsync();
```

### From KnowledgeEdge to IGraphEdge

Use the new typed edge system for creating edges:

```csharp
// Create edges using GraphEdgeFactory
var edge = GraphEdgeFactory.Create(
    sessionId: "session-1",
    fromId: "node-a",
    toId: "node-b",
    type: EdgeType.KnowledgeDependsOnKnowledge);
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
