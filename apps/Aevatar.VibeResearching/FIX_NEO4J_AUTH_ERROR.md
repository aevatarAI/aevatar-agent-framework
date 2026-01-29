# Neo4j 认证错误修复指南

## 🔴 错误信息

```
[DagStore] Failed to query Neo4j for global DAG: dagId=global, error=The client has provided incorrect authentication details too many times in a row.
```

## 💡 问题原因

**Neo4j 密码不正确**，并且因为多次失败尝试，Neo4j 可能已经暂时锁定了账户。

## ✅ 解决方案

### 方案 1: 使用 change_neo4j_password.sh 修改密码（如果知道当前密码）

**步骤**:

1. **运行密码修改脚本**:
   ```bash
   cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching
   ./change_neo4j_password.sh
   ```

2. **按提示输入**:
   - 当前密码（如果你知道）
   - 新密码
   - 确认新密码

3. **更新 boot.sh**:
   ```bash
   # 编辑 boot.sh 第 144 行，将密码改为新密码
   export NEO4J_PASSWORD="${NEO4J_PASSWORD:-你的新密码}"
   ```

4. **重启后端**:
   ```bash
   ./boot.sh
   ```

---

### 方案 2: 重置 Neo4j 密码（如果忘记密码）

**⚠️ 警告**: 这会重置数据库！

**步骤**:

1. **运行重置脚本**:
   ```bash
   cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching
   ./reset_neo4j_password.sh
   ```

2. **或者手动重置**:
   ```bash
   # 停止 Neo4j
   neo4j stop
   
   # 删除认证文件（macOS Homebrew）
   rm /opt/homebrew/var/neo4j/data/dbms/auth
   
   # 或者如果使用其他安装方式
   # rm /usr/local/var/neo4j/data/dbms/auth
   
   # 启动 Neo4j
   neo4j start
   ```

3. **首次登录设置新密码**:
   - 打开 Neo4j Browser: http://localhost:7474
   - 用户名: `neo4j`
   - 初始密码: `neo4j`（首次登录会要求修改）

4. **更新 boot.sh**:
   ```bash
   # 编辑 boot.sh 第 144 行
   export NEO4J_PASSWORD="${NEO4J_PASSWORD:-你设置的新密码}"
   ```

5. **重启后端**:
   ```bash
   ./boot.sh
   ```

---

### 方案 3: 使用环境变量临时设置密码

**适用场景**: 临时测试，不想修改 boot.sh

**步骤**:

1. **设置环境变量**:
   ```bash
   export NEO4J_PASSWORD="你的实际密码"
   ```

2. **运行 boot.sh**:
   ```bash
   ./boot.sh
   ```

**注意**: 
- 这种方式只在当前终端会话有效
- 关闭终端后需要重新设置
- 不推荐用于生产环境

---

## 🔍 验证修复

### 方法 1: 使用 cypher-shell

```bash
echo "RETURN 1 as test;" | \
  cypher-shell -u neo4j -p '你的密码' -a bolt://localhost:7687
```

**成功输出**: 应该看到 `1` 或类似的结果

**失败输出**: 会显示认证错误

---

### 方法 2: 使用 HTTP API

```bash
curl -u neo4j:你的密码 http://localhost:7474/user/neo4j
```

**成功输出**: 返回用户信息 JSON

**失败输出**: 返回 401 Unauthorized

---

### 方法 3: 检查后端日志

```bash
# 查看日志，确认没有认证错误
grep -i "authentication\|unauthorized\|Failed to query Neo4j" logs/*.log | tail -20
```

**应该看到**:
- ✅ `[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=...`
- ❌ 不应该看到: `Failed to query Neo4j for global DAG: ... authentication failure`

---

## 📋 当前配置

**boot.sh 中的密码** (第 144 行):
```bash
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI}"
```

**Neo4j 状态**: 正在运行（pid 36616）

**连接 URI**: `bolt://localhost:7687`

**用户名**: `neo4j`

---

## 💡 推荐操作步骤

1. ✅ **尝试方案 1**（如果知道当前密码）
   - 运行 `./change_neo4j_password.sh`
   - 设置新密码
   - 更新 boot.sh

2. ✅ **如果不知道密码，使用方案 2**
   - 运行 `./reset_neo4j_password.sh`
   - 重置后设置新密码
   - 更新 boot.sh

3. ✅ **验证修复**
   - 使用 cypher-shell 或 HTTP API 测试连接
   - 检查后端日志确认没有错误

4. ✅ **重启后端**
   - 运行 `./boot.sh`
   - 确认 DAG 查询正常工作

---

*最后更新: 2025-01-28*
