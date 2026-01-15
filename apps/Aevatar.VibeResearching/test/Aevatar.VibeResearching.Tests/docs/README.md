## VibeResearching.Tests

该目录包含 `Aevatar.VibeResearching` 子系统的关键单元测试（不依赖外部服务，快速稳定）。

### 关注点

- **DAG Explain**：依赖闭包、拓扑顺序、cycle 检测、provable/missing 判定
- **TraceStore**：`trace.jsonl` append-only 写入与 bounded tail 读取（latest N），以及 `runs/{runId}/summary.md` 落盘

### 文件

```
apps/Aevatar.VibeResearching/test/VibeResearching.Tests/
├── VibeResearching.Tests.csproj
├── DagExplainTests.cs
├── TraceStoreTests.cs
└── docs/
   └── README.md
```

### 运行

```bash
dotnet test apps/Aevatar.VibeResearching/test/VibeResearching.Tests/VibeResearching.Tests.csproj
```


