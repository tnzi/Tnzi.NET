# Tnzi.AI

企业级 AI 集成模块，基于 Microsoft.Extensions.AI 的自定义 Agent 引擎，提供多提供商支持、工具调用（Function Calling）、会话管理等完整功能。

## 特性

- ✅ **多提供商支持** - OpenAI（BaseUrl 可选）、Azure OpenAI、任意 OpenAI 兼容端点（DeepSeek、Ollama 等，配置 BaseUrl + ApiKey + DefaultModel）
- ✅ **自定义引擎** - 基于 Microsoft.Extensions.AI 的自定义 Agent 引擎
- ✅ **工具系统** - 基于特性的 Function Calling，支持自动扫描和按需加载
- ✅ **会话管理** - 完整的多轮对话线程管理
- ✅ **Agent 编排** - 支持 Agent 执行、工作流编排（基于 WorkflowRunner）
- ✅ **流式响应** - 支持流式输出
- ✅ **嵌入服务** - IEmbeddingService 基于 OpenAI 兼容 `/embeddings` 接口，供 RAG/向量检索使用
- ✅ **历史压缩** - 可选 ConversationContext + Prune/Summarize 压缩策略，避免双写
- ✅ **Skills** - SKILL.md 扫描与注入（Instructions 或按需工具 skill_search/skill_get），可配置 InjectionMode
- ✅ **工具审批** - IToolApprovalHandler 与 ApprovalToolWrapper，支持 human-in-the-loop（仅对 ToolRegistry 注册的工具包装；AlwaysRequireApprovalGroups 依赖 ToolDefinition.GroupName）
- ✅ **结构化输出** - IStructuredOutputService 强类型 JSON 输出，可选 UseAgent 模式
- ✅ **可观测性** - AIActivitySource 等 OpenTelemetry 集成

## 快速开始

### 1. 添加模块依赖

```csharp
[DependsOn(typeof(AIModule))]
public class MyApplicationModule : TnziApplicationModule
{
}
```

### 2. 配置

```json
{
  "AI": {
    "DefaultProvider": "OpenAI",
    "Providers": {
      "OpenAI": {
        "Enabled": true,
        "ApiKey": "your-api-key",
        "BaseUrl": "https://api.openai.com/v1",
        "DefaultModel": "gpt-4o"
      }
    }
  }
}
```

OpenAI 的 `BaseUrl` 可省略（使用默认 endpoint）；AzureOpenAI 及 DeepSeek、Ollama 等需配置 `BaseUrl`。

### 3. 使用

```csharp
public class MyService
{
    private readonly IChatService _chatService;
    
    public async Task<string> ChatAsync(string prompt)
    {
        var request = new ChatRequestDto { Message = prompt };
        var result = await _chatService.ChatAsync(request);
        return result.Data!.Content;
    }
}
```

## 架构概览

### 引擎架构说明

- **基于 Microsoft.Extensions.AI**：核心功能基于 M.E.AI 抽象层 + 自定义 Agent 引擎构建
  - `AgentExecutor` - 自定义 Agent 执行器
  - `ConversationContext` - 对话上下文管理
  - `WorkflowRunner` - 工作流执行器
  - `AITool/AIFunction` - M.E.AI 工具系统

- **Tnzi 扩展**：
  - `ChatClientFactory` - 多提供商 ChatClient 工厂
  - `AgentFactory` - 创建 AgentExecutor 实例
  - `WorkflowBuilderFactory` - 构建工作流
  - `ToolRegistry` - 工具注册和管理
  - `ToolAdapter` - 将 Tnzi 工具转换为 M.E.AI AITool

```
Tnzi.AI
├── Infrastructure/          # 基础设施层
│   ├── ChatClientFactory.cs     # ChatClient 工厂（多提供商支持）
│   ├── AgentFactory.cs          # Agent 工厂（创建 AgentExecutor）
│   ├── WorkflowBuilderFactory.cs # 工作流构建工厂
│   └── ToolRegistry.cs          # 工具注册表
├── Services/                # 业务服务层
│   ├── ChatService.cs           # 聊天服务（基于 AgentExecutor）
│   ├── AgentService.cs          # Agent 管理服务
│   ├── AgentThreadService.cs    # 线程管理服务
│   ├── WorkflowService.cs       # 工作流服务（基于 WorkflowRunner）
│   └── EmbeddingService.cs      # 嵌入服务（OpenAI 兼容 /embeddings）
├── Controllers/             # 控制器基类
│   ├── ChatControllerBase.cs       # 聊天 API
│   └── Admin/                      # 管理端 API
│       ├── AgentAdminControllerBase.cs   # Agent 管理 (CRUD + Clone)
│       ├── WorkflowAdminControllerBase.cs # 工作流管理
│       ├── QuotaAdminControllerBase.cs    # 配额管理
│       ├── ThreadAdminControllerBase.cs   # Thread 管理 (列表/详情/改标题)
│       ├── UsageAnalyticsAdminControllerBase.cs # 使用量分析
│       └── ProviderAdminControllerBase.cs # Provider 信息查询
├── Tools/                   # 工具系统
│   ├── ToolAdapter.cs     # 工具适配器
│   └── Examples/                # 内置工具示例
├── Entities/                # 数据实体
├── Dtos/                    # 数据传输对象
└── Options/                 # 配置选项
```

## 核心服务

### IChatService

聊天服务，提供对话功能。

```csharp
// 简单对话
var request = new ChatRequestDto { Message = "你好" };
var result = await _chatService.ChatAsync(request);

// 流式响应
await foreach (var chunk in _chatService.ChatStreamingAsync(request))
{
    Console.Write(chunk.Content);
}
```

### IAgentService

Agent 管理服务，支持创建、配置和运行 Agent。

```csharp
// 创建 Agent
var agent = await _agentService.CreateAsync(new CreateAgentDto
{
    Name = "助手",
    Provider = "OpenAI",
    Model = "gpt-4o",
    Instructions = "你是一个友好的助手"
});

// 运行 Agent
var response = await _agentService.RunAsync(agent.Data!.Id, "你好");
```

### IWorkflowService

工作流服务，支持 Agent 编排。

```csharp
// 创建工作流
var workflow = await _workflowService.CreateAsync(new CreateWorkflowDefinitionDto
{
    Name = "研究工作流",
    ExecutionMode = "Sequential",
    Steps = new List<WorkflowStepDto>
    {
        new() { AgentId = researchAgentId, Order = 1 },
        new() { AgentId = summaryAgentId, Order = 2 }
    }
});

// 运行工作流
var result = await _workflowService.RunAsync(workflow.Data!.Id, "分析AI发展趋势");
```

### IEmbeddingService

嵌入服务，基于 OpenAI 兼容的 `/embeddings` 接口生成文本向量，供 RAG、向量检索使用。

```csharp
// 单条
var result = await _embeddingService.GenerateEmbeddingAsync("要嵌入的文本");

// 批量或指定提供商/模型
var batch = await _embeddingService.GenerateEmbeddingsAsync(
    new List<string> { "文本1", "文本2" },
    new EmbeddingOptions { Provider = "OpenAI", Model = "text-embedding-3-small" });
```

## 工具开发

### 定义工具

```csharp
[AIToolGroup("weather", "天气工具")]
public class WeatherTools : IAIToolProvider
{
    [AIFunction("get_weather", "获取城市天气")]
    public async Task<object> GetWeatherAsync(
        [AIParameter("city", "城市名称")]
        string city)
    {
        var weather = await _weatherService.GetAsync(city);
        return new
        {
            city,
            temperature = weather.Temperature,
            condition = weather.Condition
        };
    }
}
```

### 使用工具

创建 Agent 时指定工具组：

```csharp
var agent = await _agentService.CreateAsync(new CreateAgentDto
{
    Name = "天气助手",
    Model = "gpt-4o",
    ToolGroups = new List<string> { "weather" }
});
```

## 内置工具

模块内置了三组常用工具：

- **datetime** - 日期时间工具（获取时间、日期计算、格式化等）
- **text** - 文本处理工具（统计、转换、提取、编码等）
- **websearch** - Web 搜索工具（通过 IWebSearchProvider 联网搜索）

## 配置选项

### 提供商配置

```json
{
  "AI": {
    "DefaultProvider": "OpenAI",
    "Providers": {
      "OpenAI": {
        "Enabled": true,
        "ApiKey": "your-api-key",
        "BaseUrl": "https://api.openai.com/v1",
        "DefaultModel": "gpt-4o",
        "TimeoutSeconds": 120,
        "MaxTokens": 4096,
        "Temperature": 0.7
      },
      "AzureOpenAI": {
        "Enabled": true,
        "ApiKey": "your-api-key",
        "BaseUrl": "https://your-resource.openai.azure.com",
        "DefaultModel": "gpt-4o"
      }
    }
  }
}
```

## 数据库表

所有表使用 `AI_` 前缀（TableNamePrefix = "AI"）。

| 表名 | 说明 |
|------|------|
| `AI_Agent` | Agent 定义（模型、系统提示、工具配置） |
| `AI_AgentThread` | Agent 会话线程 |
| `AI_AgentThreadMessage` | 会话消息 |
| `AI_WorkflowDefinition` | DAG 工作流定义 |
| `AI_UserQuota` | 用户用量配额 |
| `AI_UsageLog` | 用量日志 |
| `AI_MemoryEntry` | Agent 记忆条目 |

## 依赖项

### 框架依赖
- Tnzi.EFCore
- Tnzi.AspNetCore
- Tnzi

### NuGet 包

- **Microsoft.Extensions.AI** - AI 抽象层
- **Microsoft.Extensions.AI.OpenAI** - OpenAI 客户端适配
- **Microsoft.Extensions.VectorData.Abstractions** - 向量数据抽象
- **Microsoft.Extensions.Http.Resilience** - HTTP 弹性策略
- **ModelContextProtocol** - MCP 协议 C# SDK

## 文档

- [AI 模块参考](../../docs/modules/ai.md)（含快速开始、配置、工具系统与注册）

## 示例

查看 `Tools/Examples/` 目录下的内置工具示例：

- `DateTimeTools.cs` - 日期时间工具
- `TextTools.cs` - 文本处理工具
- `WebSearchTools.cs` - Web 搜索工具

## License

MIT
