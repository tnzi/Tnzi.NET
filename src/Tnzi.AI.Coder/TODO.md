# Tnzi.AI.Coder 待解决问题

> 更新: 2026-02-26 | 涉及 Tnzi.AI / Tnzi.AI.Coder / Tnzi.AI.Rag 三模块协作

---

## 已完成

### ~~1. WebTools 联网搜索能力下沉~~ (2026-02-26 完成)

**方案**: 未采用原始的"整体迁移 WebTools"方案，而是创建了 `IWebSearchProvider` 抽象层：

- `IWebSearchProvider` 接口定义在 `src/Tnzi/AI/WebSearch/` (核心库)
- `DuckDuckGoSearchProvider` 默认实现在 `src/Tnzi.AI.Coder/WebSearch/`
- `WebSearchTools` 内置工具在 `src/Tnzi.AI/Tools/Examples/` (通过 IWebSearchProvider)
- AI.Coder 的 `WebTools.web_search` 也委托给 IWebSearchProvider

**效果**: Web 应用只需 Tnzi.AI 即可使用联网搜索（注册自己的 IWebSearchProvider 实现），无需引入 AI.Coder。

### ~~2. 框架规范审计~~ (2026-02-26 完成)

- 所有 25 个文件零违规
- 所有构造函数 Check.NotNull 验证完备
- 所有服务手动注册 (无 IScopedDependency)

### ~~3. 单元测试覆盖~~ (2026-02-26 完成)

- WebSearchProviderTests (6 tests)
- FileMemoryStoreTests (15 tests)
- EnvironmentFilterTests (4 tests)
- ShellToolsTests (8 tests)
- GitToolsTests (8 tests)
- FileSystemToolsTests (8 tests)
- CodeSearchToolsTests (6 tests)
- 共 ~60 个工具相关测试，AI 模块测试总数 132/132 通过

### ~~4. HTML 架构文档更新~~ (2026-02-26 完成)

- 修正 "Tnzi.AIAgent" → "Tnzi.AI.Coder"
- 补充 Guardrails、DAG Workflow、Handoff/GroupChat 等新架构信息
- 更新所有 10 个工具组列表

### ~~5. IMemoryStore 生命周期冲突~~ (2026-02-26 完成)

**方案**: `RemoveAll + AddSingleton` 改为 `TryAddSingleton`，LoadOrder 自然决定优先级：

- 只用 AI (50): DatabaseMemoryStore (Scoped) 生效
- 只用 AI.Coder (51): FileMemoryStore (Singleton) 生效
- 两者同时: AI 先注册，DatabaseMemoryStore 生效
- 应用层可自行覆盖

### ~~6. RAG 异步文档摄取~~ (2026-02-26 完成)

**方案**: `IBackgroundJobManager` 可选依赖 + 后台任务：

- `DocumentIngestionBackgroundJob` 实现 `IBackgroundJob<DocumentIngestionJobArgs>`
- KnowledgeBaseService.UploadDocumentAsync: 有 Hangfire → 入队异步；无 → 同步回退
- 新增 `GetDocumentStatusAsync` 端点用于轮询

### ~~7. RAG IReranker 重排序器~~ (2026-02-26 完成)

**方案**: `IReranker` 接口 + `NoOpReranker` 默认透传实现：

- KnowledgeBaseService.SearchAsync / SearchAllAsync 集成 reranker
- VectorTextSearchService 集成 reranker
- 用户可注册 Cohere/Jina/bge-reranker 替换

### ~~8. RAG OpenTelemetry 可观测性~~ (2026-02-26 完成)

**方案**: `RagActivitySource` 静态类（照搬 AIActivitySource 模式）：

- ActivitySource "Tnzi.AI.Rag" + Meter
- 指标: ingestion.count, ingestion.chunk.count, search.count, error.count
- 直方图: ingestion.duration, search.duration
- 已集成到 DocumentIngestionService, KnowledgeBaseService, PgVectorStore

---

## 待处理

### 9. DuckDuckGo 搜索脆弱性 (P2 — 仅记录)

**问题**: `DuckDuckGoSearchProvider` 通过 HTML 解析 DuckDuckGo 搜索结果，依赖 DOM 结构，易碎。

**建议**:

- 当前实现作为零成本默认方案保留
- 用户可注册商业搜索 API 实现 (Bing API, Google Custom Search, Tavily, etc.)
- 考虑添加 `BingSearchProvider` 或 `TavilySearchProvider` 作为可选包

### 10. RAG 混合搜索 (P3 — 延后)

**原因延后**: schema 变更风险高 + PostgreSQL FTS (`tsvector`) 强绑定违反数据库无关原则。
IReranker 已能显著改善搜索质量，商业 reranker 可解决大部分相关性问题。

---

## 三模块组合场景

| 场景                         | 模块组合                                    |
| ---------------------------- | ------------------------------------------- |
| Web 应用（AI 对话 + 联网） | Tnzi.AI (+ 自定义 IWebSearchProvider) |
| Web 应用 + RAG 知识库 | Tnzi.AI + Tnzi.AI.Rag |
| 本地 Coder Agent | Tnzi.AI + Tnzi.AI.Coder |
| 全功能 Bot | Tnzi.AI + Tnzi.AI.Rag + Tnzi.AI.Coder |
