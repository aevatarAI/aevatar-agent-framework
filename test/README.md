# Testing Guidelines

## 📋 Overview

This directory contains all test projects for the Aevatar Agent Framework.

---

## 🧪 Test Structure

```
test/
├── Aevatar.Agents.TestBase/          # Shared test infrastructure
│   └── ClusterFixture.cs             # Orleans test cluster setup
├── Aevatar.Agents.Core.Tests/        # Core functionality tests
├── Aevatar.Agents.Local.Tests/       # Local runtime tests
├── Aevatar.Agents.Orleans.Tests/     # Orleans runtime tests
├── Aevatar.Agents.ProtoActor.Tests/  # ProtoActor runtime tests
├── ScientificResearchAssistant.Tests/ # ScientificResearchAssistant (vibe) unit tests
└── Aevatar.Agents.Orleans.MongoDB.Tests/  # MongoDB repository tests
```

---

## ✅ Unified Testing Approach

### Problem Before

Each interface required a separate in-memory implementation for testing:
- `IEventStore` → `InMemoryEventStore` (Core)
- `IEventRepository` → `InMemoryEventRepository` (TestBase)
- Every new interface → New in-memory implementation ❌

### Solution: Unified Test Extensions

**All in-memory implementations now live in their respective runtime packages**, not in TestBase:

```
src/Aevatar.Agents.Orleans/EventSourcing/
├── IEventRepository.cs              # Interface
├── InMemoryEventRepository.cs       # ✅ In-memory implementation (for tests)
├── EventSourcingTestExtensions.cs   # ✅ Unified registration
└── OrleansEventStore.cs             # Production implementation
```

---

## 🚀 Usage

### In Test Projects

**Simply call `.AddInMemoryEventSourcing()`**:

```csharp
// In ClusterFixture.cs or test setup
hostBuilder.ConfigureServices(services =>
{
    // ✅ One line registers everything
    services.AddInMemoryEventSourcing();
});
```

This automatically registers:
- `InMemoryEventRepository` as `IEventRepository`
- `OrleansEventStore` as `IEventStore`

### In Test Assertions

Access the in-memory repository for assertions:

```csharp
[Fact]
public async Task MyTest()
{
    var repository = ServiceProvider.GetInMemoryEventRepository();
    
    // Do test operations...
    await agent.DepositAsync(100);
    
    // Assert on in-memory data
    Assert.Equal(1, repository.GetTotalEventCount());
}
```

---

## 🎯 Benefits

### ✅ Single Source of Truth
- In-memory implementations live next to their interfaces
- No duplication across test projects

### ✅ Easy to Extend
- New interface? Add in-memory implementation in the same package
- Update `AddInMemoryEventSourcing()` to register it
- All tests automatically use it

### ✅ Production-like Testing
- Same `OrleansEventStore` logic as production
- Only storage backend changes (memory vs MongoDB)

### ✅ Fast & Isolated
- No database dependencies
- Each test gets a fresh in-memory instance
- Parallel test execution

---

## 📦 Test Categories

### Unit Tests
**Purpose**: Test individual components in isolation  
**Example**: `MongoEventRepositoryTests.cs`  
**Approach**: Mock dependencies with Moq

```csharp
var mockClient = new Mock<IMongoClient>();
var repository = new MongoEventRepository(mockClient.Object, options, logger);
```

### Integration Tests
**Purpose**: Test components working together  
**Example**: `OrleansEventStoreTests.cs`  
**Approach**: Use in-memory implementations

```csharp
services.AddInMemoryEventSourcing();  // ✅ Unified approach
```

### End-to-End Tests
**Purpose**: Test full user scenarios  
**Example**: Sample applications in `examples/`  
**Approach**: Use real implementations or Docker-based dependencies

---

## 🔧 Adding New Tests

### 1. Create Test Project

```bash
dotnet new xunit -n Aevatar.Agents.MyFeature.Tests
dotnet sln add test/Aevatar.Agents.MyFeature.Tests
```

### 2. Reference TestBase

```xml
<ItemGroup>
  <ProjectReference Include="..\Aevatar.Agents.TestBase\Aevatar.Agents.TestBase.csproj" />
</ItemGroup>
```

### 3. Use Shared Test Infrastructure

```csharp
public class MyTests : AevatarAgentsTestBase
{
    // Inherit from AevatarAgentsTestBase for Orleans tests
    // Or use standalone tests for unit tests
}
```

---

## 🏃 Running Tests

### All Tests
```bash
dotnet test
```

### Specific Project
```bash
dotnet test test/Aevatar.Agents.Orleans.Tests/
```

### With Code Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Filtered by Name
```bash
dotnet test --filter "FullyQualifiedName~EventSourcing"
```

---

## 📊 Test Status

| Test Project | Status | Coverage |
|-------------|--------|----------|
| Core.Tests | ✅ 97% (115/118) | ~85% |
| Local.Tests | ✅ 91% (21/23) | ~80% |
| Orleans.Tests | ✅ 86% (25/29) | ~75% |
| ProtoActor.Tests | ✅ 100% (21/21) | ~85% |
| Orleans.MongoDB.Tests | ✅ 100% (11/11) | ~90% |

---

## 🐛 Debugging Tests

### Visual Studio
- Set breakpoints in test methods
- Right-click test → Debug Test

### VS Code
- Use `.vscode/launch.json` configuration
- Set `"justMyCode": false` to debug framework code

### Command Line
```bash
# Run with detailed output
dotnet test --logger:"console;verbosity=detailed"

# Run specific test
dotnet test --filter "FullyQualifiedName=Aevatar.Agents.Orleans.Tests.EventSourcing.OrleansEventStoreTests.AppendEventsAsync_ShouldAppendEvents"
```

---

## 📝 Best Practices

### ✅ DO
- Use `AddInMemoryEventSourcing()` for EventSourcing tests
- Mock external dependencies (MongoDB, HTTP clients)
- Write descriptive test names
- Test both happy and error paths
- Clean up resources in `Dispose()`

### ❌ DON'T
- Don't create duplicate in-memory implementations
- Don't depend on test execution order
- Don't use Thread.Sleep (use async properly)
- Don't commit real connection strings

---

## 🔗 Related Documentation

- [Agent Factory Usage](../docs/Agent_Factory_Usage.md)
- [EventSourcing Design](../docs/EVENTSOURCING_DESIGN.md)
- [Stream Architecture](../docs/STREAM_ARCHITECTURE.md)

---

**Last Updated**: 2025-11-11  
**Status**: ✅ Active
