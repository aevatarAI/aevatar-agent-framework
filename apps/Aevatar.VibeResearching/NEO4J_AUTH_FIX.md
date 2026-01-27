# Neo4j 认证失败问题修复指南

## 🔴 问题

日志中出现：
```
[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=The client is unauthorized due to authentication failure.
```

## 💡 根本原因

**Neo4j 密码不匹配**，导致后端无法连接 Neo4j，因此：
- 无法查询 DAG（Plan 节点和 Knowledge 节点）
- 无法创建新的 DAG 节点
- DAG 始终为空（0 节点）

## 🔍 当前配置

- **boot.sh 中的密码**: `subfyz-naqmug-8Gemga`
- **Neo4j 状态**: 正在运行（pid 922）
- **连接 URI**: `bolt://localhost:7687`
- **用户名**: `neo4j`

## ✅ 解决方案

### 方案 1: 确认并更新 boot.sh 中的密码（推荐）

**步骤**:

1. **确认 Neo4j 的实际密码**
   - 如果你知道密码，直接使用
   - 如果忘记，需要重置（见方案 3）

2. **修改 boot.sh**
   ```bash
   # 编辑 boot.sh 第 144 行
   export NEO4J_PASSWORD="${NEO4J_PASSWORD:-你的实际密码}"
   ```

3. **重启后端**
   ```bash
   # 停止当前后端
   # 然后重新运行
   ./boot.sh
   ```

### 方案 2: 使用环境变量（临时）

**步骤**:

1. **设置环境变量**
   ```bash
   export NEO4J_PASSWORD="你的实际密码"
   ```

2. **运行 boot.sh**
   ```bash
   ./boot.sh
   ```

**注意**: 这种方式只在当前终端会话有效，关闭终端后需要重新设置。

### 方案 3: 重置 Neo4j 密码

**如果忘记 Neo4j 密码**:

#### 方法 A: 通过 Neo4j Browser（如果还能访问）

1. 打开 Neo4j Browser: http://localhost:7474
2. 使用当前密码登录
3. 运行以下 Cypher 命令修改密码：
   ```cypher
   ALTER CURRENT USER SET PASSWORD FROM '旧密码' TO '新密码';
   ```
   或者：
   ```cypher
   ALTER USER neo4j SET PASSWORD '新密码';
   ```

#### 方法 B: 重置认证文件（如果完全无法访问）

**⚠️ 警告**: 这会删除所有用户和权限设置！

1. **停止 Neo4j**
   ```bash
   neo4j stop
   ```

2. **找到 Neo4j 数据目录**
   - Homebrew: `/opt/homebrew/var/neo4j/data/dbms`
   - Docker: 取决于容器配置
   - 手动安装: `<neo4j-home>/data/dbms`

3. **删除认证文件**
   ```bash
   # Homebrew 示例
   rm /opt/homebrew/var/neo4j/data/dbms/auth
   ```

4. **重启 Neo4j**
   ```bash
   neo4j start
   ```

5. **首次登录设置新密码**
   - 打开 Neo4j Browser: http://localhost:7474
   - 初始用户名: `neo4j`
   - 初始密码: `neo4j`（首次登录会要求修改）
   - 设置新密码为: `subfyz-naqmug-8Gemga`（或你想要的密码）

6. **更新 boot.sh**
   ```bash
   export NEO4J_PASSWORD="${NEO4J_PASSWORD:-新密码}"
   ```

### 方案 4: 修改 Neo4j 密码为 boot.sh 中的密码

**如果你能访问 Neo4j**:

1. 打开 Neo4j Browser: http://localhost:7474
2. 登录
3. 运行：
   ```cypher
   ALTER USER neo4j SET PASSWORD 'subfyz-naqmug-8Gemga';
   ```

## 🧪 测试连接

修复后，测试 Neo4j 连接：

```bash
# 使用 cypher-shell
echo "RETURN 1 as test;" | cypher-shell -u neo4j -p '你的密码' -a bolt://localhost:7687

# 或使用 HTTP API
curl -u neo4j:你的密码 http://localhost:7474/user/neo4j
```

如果连接成功，应该看到返回结果。

## 📋 验证修复

修复后，检查：

1. **后端日志中不再有认证错误**
   ```
   [DagStore] Failed to query Neo4j for global DAG: ... authentication failure
   ```
   应该消失

2. **DAG API 可以正常查询**
   ```bash
   curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'
   ```
   应该返回节点数量（可能为 0，但不会报错）

3. **Plan 节点可以创建**
   - 运行 research round
   - 检查日志: `[DagStore] Creating PlanNode`
   - 查询 DAG: `curl http://localhost:5678/api/dag/global | jq '.dag.nodes[] | select(.kind == "Plan")'`

## 🔄 重启后端

修复密码后，**必须重启后端**才能生效：

1. 停止当前后端进程
2. 重新运行 `./boot.sh`

## 💡 建议

1. **使用一致的密码管理**
   - 将 Neo4j 密码保存在安全的地方
   - 确保 boot.sh 中的密码与实际密码一致

2. **使用环境变量（生产环境）**
   - 不要将密码硬编码在 boot.sh 中
   - 使用环境变量或密钥管理服务

3. **定期检查连接**
   - 运行诊断脚本: `./check_backend_and_dag.sh`
   - 检查日志中的 Neo4j 错误
