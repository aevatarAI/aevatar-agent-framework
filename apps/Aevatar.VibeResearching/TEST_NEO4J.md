# Neo4j 测试指南

## 🧪 测试 Neo4j 是否正常运行

本文档提供多种方法来测试 Neo4j 是否正常运行。

---

## 方法 1: 使用项目中的验证脚本（推荐）

### 1.1 使用 verify_neo4j_connection.sh

```bash
cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching
./verify_neo4j_connection.sh
```

**功能**:
- ✅ 测试 Bolt 连接
- ✅ 测试查询功能
- ✅ 检查环境变量配置
- ✅ 显示当前节点数

**注意**: 脚本中硬编码了密码，可能需要修改。

---

### 1.2 使用 check_neo4j_connection.sh

```bash
cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching
./check_neo4j_connection.sh
```

**功能**:
- ✅ 检查 HTTP 端口 (7474)
- ✅ 检查 Bolt 端口 (7687)
- ✅ 检查环境变量
- ✅ 检查 Neo4j 配置
- ✅ 尝试 cypher-shell 连接

---

## 方法 2: 检查服务状态

### 2.1 检查 Neo4j 进程

```bash
neo4j status
```

**预期输出**:
```
Neo4j is running at pid <PID>
```

**如果未运行**:
```bash
neo4j start
```

---

### 2.2 检查进程（备用方法）

```bash
ps aux | grep neo4j
```

**预期输出**: 应该看到 Neo4j 相关进程

---

## 方法 3: 测试端口连接

### 3.1 测试 HTTP 端口 (7474)

```bash
curl http://localhost:7474
```

**预期输出**: 返回 JSON，包含 `neo4j_version` 字段

**示例**:
```json
{
  "bolt_routing": "neo4j://localhost:7687",
  "transaction": "http://localhost:7474/db/neo4j/tx",
  "bolt": "bolt://localhost:7687",
  "neo4j_version": "5.x.x",
  "neo4j_edition": "community"
}
```

---

### 3.2 测试 Bolt 端口 (7687)

```bash
# 使用 nc (netcat)
nc -zv localhost 7687

# 或使用 telnet
telnet localhost 7687
```

**预期输出**: `Connection succeeded` 或连接成功

---

## 方法 4: 使用 cypher-shell 测试连接

### 4.1 基本连接测试

```bash
echo "RETURN 1 as test;" | \
  cypher-shell -u neo4j -p '你的密码' -a bolt://localhost:7687
```

**预期输出**: `1`

**如果失败**: 会显示认证错误或连接错误

---

### 4.2 测试查询功能

```bash
# 查询节点数量
echo "MATCH (n) RETURN count(n) as nodeCount;" | \
  cypher-shell -u neo4j -p '你的密码' -a bolt://localhost:7687 --format plain

# 查询前 10 个节点
echo "MATCH (n) RETURN n LIMIT 10;" | \
  cypher-shell -u neo4j -p '你的密码' -a bolt://localhost:7687 --format plain
```

---

### 4.3 交互式连接

```bash
cypher-shell -u neo4j -p '你的密码' -a bolt://localhost:7687
```

**进入交互模式后**:
```cypher
RETURN 1;
MATCH (n) RETURN count(n);
```

**退出**: 输入 `:exit` 或按 `Ctrl+D`

---

## 方法 5: 使用 HTTP API 测试

### 5.1 测试用户认证

```bash
curl -u neo4j:你的密码 http://localhost:7474/user/neo4j
```

**预期输出**: 返回用户信息的 JSON

**如果失败**: 返回 `401 Unauthorized`

---

### 5.2 执行 Cypher 查询（HTTP）

```bash
curl -X POST http://localhost:7474/db/neo4j/tx/commit \
  -u neo4j:你的密码 \
  -H "Content-Type: application/json" \
  -d '{
    "statements": [{
      "statement": "RETURN 1 as test"
    }]
  }'
```

**预期输出**: 返回查询结果的 JSON

---

## 方法 6: 使用 Neo4j Browser（图形界面）

### 6.1 打开 Neo4j Browser

在浏览器中打开: **http://localhost:7474**

### 6.2 登录

- **用户名**: `neo4j`
- **密码**: 你的 Neo4j 密码

### 6.3 测试查询

在查询框中输入:
```cypher
RETURN 1 as test;
```

或:
```cypher
MATCH (n) RETURN count(n) as nodeCount;
```

**预期结果**: 显示查询结果

---

## 方法 7: 检查日志

### 7.1 查看 Neo4j 日志

```bash
# Homebrew 安装的 Neo4j
tail -f /opt/homebrew/var/log/neo4j/neo4j.log

# 或使用 brew services
brew services info neo4j
```

**查找**:
- ✅ `Started` - 服务已启动
- ✅ `Bolt enabled on 0.0.0.0:7687` - Bolt 端口已启用
- ❌ `ERROR` - 错误信息

---

## 📋 快速测试清单

运行以下命令快速测试 Neo4j:

```bash
cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching

# 1. 检查服务状态
echo "=== 1. 服务状态 ==="
neo4j status

# 2. 测试 HTTP 端口
echo ""
echo "=== 2. HTTP 端口测试 ==="
curl -s http://localhost:7474 | grep -o '"neo4j_version":"[^"]*"' || echo "HTTP 端口不可访问"

# 3. 测试 Bolt 端口
echo ""
echo "=== 3. Bolt 端口测试 ==="
nc -zv localhost 7687 2>&1 | grep -q "succeeded" && echo "✅ Bolt 端口可访问" || echo "❌ Bolt 端口不可访问"

# 4. 测试连接（需要密码）
echo ""
echo "=== 4. 连接测试 ==="
PASSWORD="${NEO4J_PASSWORD:-gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI}"
if echo "RETURN 1;" | cypher-shell -u neo4j -p "$PASSWORD" -a bolt://localhost:7687 2>&1 | grep -q "1"; then
    echo "✅ 连接成功"
else
    echo "❌ 连接失败（可能是密码错误）"
fi
```

---

## 🔍 常见问题排查

### 问题 1: Neo4j 未运行

**症状**: `neo4j status` 显示未运行

**解决**:
```bash
neo4j start
# 等待 10-30 秒
neo4j status
```

---

### 问题 2: 端口不可访问

**症状**: `nc -zv localhost 7687` 失败

**检查**:
1. Neo4j 是否完全启动（等待 30 秒）
2. 防火墙设置
3. Neo4j 配置文件中的监听地址

---

### 问题 3: 认证失败

**症状**: `The client is unauthorized due to authentication failure`

**解决**:
1. 检查密码是否正确
2. 使用 `./change_neo4j_password.sh` 修改密码
3. 或使用 `./reset_neo4j_password.sh` 重置密码

---

### 问题 4: 连接被拒绝

**症状**: `Connection refused`

**解决**:
1. 检查 Neo4j 是否运行: `neo4j status`
2. 检查端口是否正确: `netstat -an | grep 7687`
3. 重启 Neo4j: `neo4j restart`

---

## ✅ 成功标准

Neo4j 正常运行应该满足以下条件:

1. ✅ `neo4j status` 显示 "Neo4j is running"
2. ✅ HTTP 端口 (7474) 可访问
3. ✅ Bolt 端口 (7687) 可访问
4. ✅ `cypher-shell` 可以连接并执行查询
5. ✅ Neo4j Browser 可以打开并登录
6. ✅ 后端应用可以连接并查询 DAG

---

## 🚀 推荐测试流程

1. **快速检查**:
   ```bash
   neo4j status
   curl http://localhost:7474
   ```

2. **完整测试**:
   ```bash
   ./check_neo4j_connection.sh
   ```

3. **验证连接**:
   ```bash
   ./verify_neo4j_connection.sh
   ```

4. **测试应用连接**:
   ```bash
   # 启动后端
   ./boot.sh
   
   # 检查日志，确认没有 Neo4j 错误
   grep -i "neo4j\|Failed to query" logs/*.log | tail -10
   ```

---

*最后更新: 2025-01-28*
