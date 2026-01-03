# Aevatar.Agents.Persistence.InMemory.Graph

InMemory provider for `Aevatar.Agents.Persistence.Graph`（开发/测试最快，无外部依赖）。

## 目录结构

```
src/Aevatar.Agents.Persistence.InMemory.Graph/
├── DependencyInjection/            # DI 扩展（AddAevatarGraphInMemory / AddAevatarGraphMemory）
├── Compilation/                    # PassThroughGraphCompiler（GraphOperation 直通）
├── Execution/                      # InMemoryGraphExecutor（执行 GraphOperation）
├── Store/                          # InMemoryGraphStore（节点/边字典）
└── docs/README.md                  # 本文档
```

## 快速开始

```csharp
using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddAevatarGraphInMemory();

await using var provider = services.BuildServiceProvider();
var graph = provider.GetRequiredService<IGraphClient>();
```


