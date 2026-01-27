# Neo4j 设置指南

## 问题诊断

DAG 图无法显示的原因是 **Neo4j Bolt 端口连接失败**。

## 已完成的修复

1. ✅ 启用了 Bolt 连接器配置
2. ✅ 修复了 `server.bolt.advertised_address` 配置错误
3. ✅ 配置了 Java 21 环境变量
4. ✅ Neo4j 服务已成功启动
5. ✅ Bolt 端口 (7687) 现在可以访问

## 当前状态

- ✅ Neo4j HTTP 端口 (7474): 可访问
- ✅ Neo4j Bolt 端口 (7687): 可访问
- ✅ Java 21: 已安装并配置

## 永久设置环境变量

为了避免每次都需要手动设置 Java 环境变量，请将以下内容添加到 `~/.zshrc`：

```bash
# Java 21 for Neo4j
export JAVA_HOME="$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home"
export PATH="$JAVA_HOME/bin:$PATH"

# Neo4j 环境变量
export NEO4J_URI="bolt://localhost:7687"
export NEO4J_USERNAME="neo4j"
export NEO4J_PASSWORD="password"  # 替换为你的实际密码
export NEO4J_DATABASE="neo4j"
```

然后运行：
```bash
source ~/.zshrc
```

## 启动 Neo4j

### 方法 1：使用启动脚本（推荐）

```bash
cd apps/Aevatar.VibeResearching
./start_neo4j_with_java.sh
```

### 方法 2：手动启动

```bash
# 设置 Java 环境变量
export JAVA_HOME="$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home"
export PATH="$JAVA_HOME/bin:$PATH"

# 启动 Neo4j
NEO4J_HOME=$(brew --prefix neo4j)
cd "$NEO4J_HOME/libexec"
./bin/neo4j start
```

### 方法 3：使用 Homebrew 服务（需要先设置环境变量）

```bash
# 先设置环境变量（见上面的永久设置）
brew services restart neo4j
```

## 验证连接

```bash
# 检查 Bolt 端口
nc -zv localhost 7687

# 检查 HTTP 端口
curl http://localhost:7474

# 检查 API
curl http://localhost:5173/api/dag/global | jq '.dag.nodes | length'
```

## 常见问题

### 1. Neo4j 服务启动失败

**原因**: Java 环境变量未设置

**解决**: 
```bash
export JAVA_HOME="$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home"
export PATH="$JAVA_HOME/bin:$PATH"
brew services restart neo4j
```

### 2. Bolt 端口连接被拒绝

**原因**: Bolt 连接器未启用或配置错误

**解决**: 检查配置文件 `/opt/homebrew/opt/neo4j/libexec/conf/neo4j.conf`：
```
server.bolt.enabled=true
server.bolt.listen_address=0.0.0.0:7687
server.bolt.advertised_address=localhost:7687  # 不能是 0.0.0.0
```

### 3. DAG 仍然为空

**原因**: 
- 没有运行过 research round
- 节点创建失败

**解决**: 
1. 确保 Neo4j 正常运行
2. 运行一个完整的 research round（包括 brief generation 和 dag_builder）
3. 查看日志中的节点创建消息

## 下一步

1. ✅ Neo4j 已启动并运行
2. ✅ Bolt 端口可访问
3. ⏭️ 运行程序，执行一个 research round
4. ⏭️ 查看日志确认节点创建成功
5. ⏭️ 验证 DAG 图显示

## 日志检查

运行程序后，查看以下日志确认节点创建：

```
[VibeOrchestrator] Applying milestones DAG mutation: sessionId=..., nodeCount=X
[DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X
[DagStore] Creating PlanNode: nodeId=..., label=...
[DagStore] PlanNode created successfully: nodeId=...
[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y, edges=Z
```
