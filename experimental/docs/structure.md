# Experimental Structure

`/experimental` 放置规划阶段或正在实现中的应用。稳定后通过 `experimental/promote.sh` 迁移到 `/apps`。

## 结构树（简化）

```
experimental/
├── notebook/
├── learning/
├── novel/
└── aevatar-kit/
```

## 约束

- 实验应用必须自带 `boot.sh` 与 `docs/TECH_DEBT.md`
- 变更成熟后再进入 `/apps`


