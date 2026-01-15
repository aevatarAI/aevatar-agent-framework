# Apps Structure

`/apps` 只放稳定、可运行的系统级应用。每个 app 必须包含：
- `README.md`
- `boot.sh`
- `<app>.slnx`
- `docs/TECH_DEBT.md`

## 结构树（简化）

```
apps/
├── Aevatar.Trading/
├── Aevatar.VibeResearching/
├── Aevatar.AxiomReasoning/
├── Aevatar.CognitiveMesh/
├── Aevatar.PaperReview/
├── Aevatar.App/
├── Aevatar.Config/
└── Aevatar.MakerSystem/
```

## 约束

- `apps/` 内的应用可以依赖 `src/`，但不得依赖 `examples/`
- 跨应用共享应沉淀为 `src/` 的公共库


