# Technology Stack

## Project Type

Monorepo 内的分布式智能体框架 + 多个产品化系统（本 steering 聚焦“科研助手平台”的可交付工程约束）。

## Core Technologies

### Primary Language(s)
- **Language**: C# (.NET 10) + TypeScript (React/Vite)
- **Runtime/Compiler**: .NET 10 SDK, Node.js

### Key Dependencies/Libraries
- **Aevatar Agent Framework**: Actor Model + event-driven agent runtime
- **AG-UI**: snapshot-first SSE 事件流协议（Run/Step/Text/State/Custom）
- **Protocol Buffers**: 跨边界类型的唯一可信 schema（state/event/config/message）

### Application Architecture

- **Event-driven (internal)**: agent 内部仍可用事件/stream 协作（runtime-agnostic）
- **File-SSoT (external)**: **文件系统是唯一事实来源**；agent 协作、写作与验证通过文件落盘完成
- **Boundary projection**: UI/AG-UI 只是投影层，必须能从文件重建

### Data Storage (if applicable)

- **Primary storage (MVP)**: 文件系统
  - `facts/`：已验证事实（可依赖）
  - `workspace/sessions/{sessionId}/facts_proposed/`：候选事实（待共识/验证）
  - `workspace/sessions/{sessionId}/paper/`：Markdown 稿件（draft/outline/citations）
  - `workspace/sessions/{sessionId}/mailbox/`：agent 间文件消息
  - `sources/`（可选）：证据库/引用摘录
- **Optional**: MemoryStore / VectorIndex（Embedding 检索）
- **Data formats**:
  - **Protobuf schema**：所有跨边界类型必须有 `.proto`
  - **Human-readable**：可用 Protobuf JSON 映射落盘为 `.json/.md`（便于 review）

### External Integrations (if applicable)

- **LLM Providers**: MEAI provider factory via `LLMProviders` config
- **MCP**: Cursor-style `mcpServers`（HTTP / stdio / Docker），由框架层 `AIGAgentBase` best-effort 自动连接与注册

### Consensus & Verification (核心闭环)

- **Soft consensus**（Maker system 风格）：
  - 多 agent 对同一个 `facts_proposed/*` 给出 vote/review
  - 达到阈值后由 promoter 产生 decision 文件并执行 promote
- **Hard verification**（一锤定音）：
  - 可执行工具（例如 Python）给出可复现输出
  - 验证通过可直接 promote（并把验证产物落盘作为证据）

### Monitoring & Dashboard Technologies (if applicable)

- **Dashboard Framework**: React + Vite + Tailwind
- **Real-time Communication**: SSE (AG-UI)

## Development Environment

### Build & Development Tools
- `dotnet build` / `dotnet test`
- `npm run dev` / `npm run build`

### Code Quality Tools
- .NET analyzers + TypeScript compiler + ESLint

## Technical Requirements & Constraints

### Security & Compliance

- **Dangerous tools default-off**：例如 `python_exec` 必须显式开启
- **Secrets hygiene**：真实密钥只允许在本地 secrets 文件，不可提交

### Scalability & Reliability

- 工程约束：任何 UI/投影逻辑 best-effort，不允许杀主流程
- snapshot-first：断线重连不依赖 replay tool/token spam

## Technical Decisions & Rationale

### Decision Log

1. **facts + sources 分层**
   - *Why*: 区分“可依赖事实”和“可引用来源”，让推理更可控、更可复用
   - *Update*: `sources/` 变为可选；核心闭环是 `facts_proposed/` → promote → `facts/`
2. **文件目录作为可审计产物**
   - *Why*: 研究过程需要复现与 review；文件系统天然支持版本化与协作
3. **Protobuf schema-first**
   - *Why*: 跨 runtime/跨 agent 的边界必须可演进、可验证、可序列化
4. **facts_proposed → consensus/verify → facts**
   - *Why*: 把“未验证推论”与“可依赖事实”隔离，避免污染推理前提
5. **Single-writer for paper/facts**
   - *Why*: 多 agent 并发写同一文件会冲突；通过 patch proposal + 单写者合并保证一致性

## Known Limitations

- 文件目录协作在并发场景下需要额外机制（原子写、锁、幂等）；MVP 先固化协议与目录结构


