# Safari 内存管理指南

## 📋 macOS Safari 内存限制

### 官方说明

**Apple 没有公开指定每个 Safari 标签页的固定内存限制**。

Safari 使用**动态内存管理**，而不是固定的每标签页限制。

---

## 🔍 Safari 内存管理机制

### 1. 自动内存管理

**机制**:
- Safari 会**自动监控**每个标签页的内存使用
- 当内存使用过高时，Safari 会**自动重新加载**页面
- 用户会看到提示："This webpage was reloaded because it was using significant memory"

**特点**:
- ✅ 动态调整，没有固定限制
- ✅ 根据系统可用内存自动调整
- ✅ 自动释放不活跃标签页的内存

---

### 2. 实际内存使用情况

**根据用户报告**:
- 单个标签页可能使用 **1GB 或更多** RAM（对于内存密集型应用）
- 内存使用取决于：
  - 网页复杂度
  - JavaScript 执行
  - 存储的数据（localStorage, cookies）
  - WebKit 进程数量

**影响因素**:
- 网页内容复杂度
- 活跃的 JavaScript 进程
- 存储的数据量
- 同时打开的标签页数量

---

## ⚠️ 内存超限的后果

### Safari 的行为

1. **自动重新加载页面**
   - 显示消息："This webpage was reloaded because it was using significant memory"
   - 页面状态会丢失（除非应用有持久化机制）

2. **标签页可能被暂停**
   - 不活跃的标签页会被暂停
   - 内存会被释放

3. **性能下降**
   - 页面响应变慢
   - 动画卡顿
   - JavaScript 执行变慢

---

## 🔧 解决方案

### 方案 1: 优化应用内存使用（推荐）

#### 1.1 限制数据量

**前端优化**:

```typescript
// 限制消息历史数量
const MAX_MESSAGES = 100;
const messages = useMemo(() => 
  allMessages.slice(-MAX_MESSAGES), 
  [allMessages]
);

// 限制 DAG 节点显示数量
const MAX_DAG_NODES = 500;
const displayedNodes = useMemo(() => 
  dagNodes.slice(0, MAX_DAG_NODES), 
  [dagNodes]
);
```

**后端配置** (`appsettings.json`):

```json
{
  "Materials": {
    "MaxContextChars": 18000,      // 限制上下文大小
    "MaxPerDocChars": 6000,        // 限制每个文档大小
    "MaxFiles": 200                // 限制文件数量
  }
}
```

---

#### 1.2 使用虚拟滚动

**代码位置**: `sisyphus-frontend/src/components/`

应用已使用 `@tanstack/react-virtual` 进行虚拟滚动：

```typescript
import { useVirtualizer } from '@tanstack/react-virtual'

// 只渲染可见区域的内容，减少 DOM 节点
const virtualizer = useVirtualizer({
  count: items.length,
  getScrollElement: () => parentRef.current,
  estimateSize: () => 50,
})
```

**效果**: 大幅减少内存使用，特别是对于长列表。

---

#### 1.3 清理未使用的数据

**代码位置**: `use-axiom-stream.ts` (第 42, 982 行)

```typescript
// 清除工具输出以防止内存泄漏
// NOTE: Cleared on session change to prevent memory leaks
```

**最佳实践**:
- 定期清理历史消息
- 清理未使用的组件状态
- 使用 `useEffect` 清理副作用

---

#### 1.4 分页加载

**实现分页**:
- 不要一次性加载所有数据
- 使用分页 API
- 按需加载（lazy loading）

**示例**:
```typescript
// 分页加载消息
const [page, setPage] = useState(1);
const messages = useQuery({
  queryKey: ['messages', sessionId, page],
  queryFn: () => fetchMessages(sessionId, page, 50)
});
```

---

### 方案 2: 减少 DOM 节点

**优化渲染**:

```typescript
// 使用 React.memo 避免不必要的重渲染
const MessageItem = React.memo(({ message }) => {
  // ...
});

// 使用 useMemo 缓存计算结果
const filteredNodes = useMemo(() => 
  nodes.filter(n => n.visible),
  [nodes]
);
```

---

### 方案 3: 优化图片和资源

**图片优化**:
- 使用适当的图片格式（WebP）
- 压缩图片大小
- 延迟加载图片（lazy loading）

**资源优化**:
- 代码分割（Code Splitting）
- 按需加载组件
- 压缩 JavaScript/CSS

---

### 方案 4: 使用 Web Workers

**将计算密集型任务移到 Worker**:

```typescript
// 在 Worker 中处理大量数据
const worker = new Worker('/workers/data-processor.js');
worker.postMessage(largeData);
worker.onmessage = (e) => {
  // 处理结果
};
```

**效果**: 减少主线程内存压力。

---

## 📊 应用中的内存优化

### 已实现的优化

1. **虚拟滚动** (`@tanstack/react-virtual`)
   - 减少 DOM 节点
   - 只渲染可见内容

2. **内存泄漏防护**
   - Session 切换时清理数据
   - Tool 输出定期清理

3. **数据限制**
   - Materials Context 限制（18,000 字符）
   - 文件大小限制（15MB）
   - 消息历史限制

---

### 可以进一步优化的地方

1. **消息历史分页**
   - 当前可能加载所有消息
   - 建议实现分页或虚拟滚动

2. **DAG 节点限制**
   - 大 DAG 可能占用大量内存
   - 建议只加载可见节点

3. **SSE 数据清理**
   - 定期清理旧的 SSE 消息
   - 限制内存中的消息数量

---

## 🔍 监控内存使用

### Safari 开发者工具

**位置**: 开发者工具 → `时间线` 或 `性能` 标签

**查看内容**:
- JavaScript 堆大小
- DOM 节点数量
- 内存使用趋势

---

### 代码中监控

**使用 Performance API**:

```typescript
// 监控内存使用
if ('memory' in performance) {
  const memory = (performance as any).memory;
  console.log('Used:', memory.usedJSHeapSize);
  console.log('Total:', memory.totalJSHeapSize);
  console.log('Limit:', memory.jsHeapSizeLimit);
}
```

**注意**: Safari 可能不支持 `performance.memory` API。

---

## 💡 最佳实践

### 1. 限制数据量

- ✅ 使用分页或虚拟滚动
- ✅ 限制历史消息数量
- ✅ 限制 DAG 节点显示数量

---

### 2. 及时清理

- ✅ 组件卸载时清理定时器
- ✅ 清理事件监听器
- ✅ 清理未使用的引用

---

### 3. 优化渲染

- ✅ 使用 `React.memo` 避免不必要的重渲染
- ✅ 使用 `useMemo` 缓存计算结果
- ✅ 使用 `useCallback` 缓存函数

---

### 4. 代码分割

- ✅ 使用动态导入 (`import()`)
- ✅ 路由级别的代码分割
- ✅ 按需加载组件

---

## 🚨 如果内存超限

### Safari 自动处理

1. **页面自动重新加载**
   - Safari 会显示提示消息
   - 页面状态会丢失

2. **应用恢复机制**

**前端恢复**:
- UI Snapshot 会恢复部分状态
- Session 数据会保留（后端）
- DAG 数据会保留（Neo4j）

**代码位置**: `SessionUiSnapshotStore.cs`

---

### 手动处理

1. **关闭其他标签页**
   - 释放系统内存
   - 减少 Safari 总体内存压力

2. **重启 Safari**
   - 完全清理内存
   - 重新开始

3. **优化应用**
   - 减少数据量
   - 实现分页
   - 使用虚拟滚动

---

## 📋 应用配置建议

### 当前配置

**后端限制** (`appsettings.json`):
```json
{
  "Materials": {
    "MaxContextChars": 18000,      // 上下文限制
    "MaxPerDocChars": 6000,        // 每个文档限制
    "MaxFiles": 200                // 文件数量限制
  }
}
```

**前端优化**:
- ✅ 使用虚拟滚动
- ✅ 内存泄漏防护
- ⚠️ 可以添加消息历史限制

---

### 建议的优化配置

**前端** (`sisyphus-frontend/src/store/sisyphus-store.ts`):

```typescript
// 限制消息历史
const MAX_MESSAGES = 100;
const messages = useMemo(() => 
  allMessages.slice(-MAX_MESSAGES),
  [allMessages]
);

// 限制 DAG 节点显示
const MAX_DISPLAYED_NODES = 500;
```

**后端** (`appsettings.json`):

```json
{
  "Materials": {
    "MaxContextChars": 10000,      // 降低上下文限制
    "MaxPerDocChars": 4000,        // 降低文档限制
    "MaxFiles": 100                // 降低文件数量
  }
}
```

---

## 🔍 诊断内存问题

### 方法 1: Safari 开发者工具

1. **打开开发者工具** (`Cmd + Option + I`)
2. **查看时间线/性能标签**
3. **查看内存使用趋势**
4. **查找内存泄漏**

---

### 方法 2: Activity Monitor

```bash
# 查看 Safari 内存使用
open -a "Activity Monitor"
# 查找 Safari 进程
# 查看内存使用情况
```

---

### 方法 3: 代码监控

```typescript
// 在关键位置添加内存监控
const logMemory = () => {
  if ('memory' in performance) {
    const mem = (performance as any).memory;
    console.log('Memory:', {
      used: (mem.usedJSHeapSize / 1024 / 1024).toFixed(2) + ' MB',
      total: (mem.totalJSHeapSize / 1024 / 1024).toFixed(2) + ' MB',
      limit: (mem.jsHeapSizeLimit / 1024 / 1024).toFixed(2) + ' MB'
    });
  }
};

// 定期监控
setInterval(logMemory, 5000);
```

---

## 📊 内存使用参考

| 数据类型 | 典型大小 | 优化建议 |
|---------|---------|---------|
| **单条消息** | 1-10 KB | 限制历史消息数量 |
| **DAG 节点** | 0.5-2 KB | 虚拟滚动，限制显示数量 |
| **图片** | 50-500 KB | 压缩，延迟加载 |
| **JavaScript 堆** | 10-100 MB | 代码分割，及时清理 |

---

## 💡 总结

### Safari 内存特点

- ✅ **没有固定限制** - 动态管理
- ✅ **自动重新加载** - 内存过高时自动处理
- ✅ **标签页暂停** - 不活跃标签页自动释放内存

### 应用优化建议

1. ✅ **限制数据量** - 使用分页和虚拟滚动
2. ✅ **及时清理** - 清理未使用的数据和引用
3. ✅ **优化渲染** - 减少 DOM 节点和重渲染
4. ✅ **代码分割** - 按需加载组件

### 如果内存超限

- Safari 会自动重新加载页面
- 应用会通过 UI Snapshot 恢复部分状态
- DAG 数据会保留（Neo4j）
- Session 数据会保留（后端）

---

## 🔗 相关文档

- `CONTEXT_BUDGET_WARNING.md` - 上下文预算警告
- `SESSION_PERSISTENCE.md` - Session 持久化
- `SAFARI_BROWSER_ANALYSIS.md` - Safari 浏览器分析

---

*最后更新: 2025-01-28*
