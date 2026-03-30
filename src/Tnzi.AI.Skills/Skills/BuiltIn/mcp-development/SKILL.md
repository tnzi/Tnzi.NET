---
name: MCP Server Development
slug: mcp-development
description: Guide for creating high-quality MCP (Model Context Protocol) servers in .NET that enable LLMs to interact with external services through well-designed tools. Use when building MCP servers to integrate external APIs or services using the ModelContextProtocol C# SDK. Covers tool design principles, input validation, error handling, security, testing, and transport configuration.
agents: "*"
---

# MCP Server Development Guide (.NET)

## Overview

Create MCP (Model Context Protocol) servers that enable LLMs to interact with external services through well-designed tools. The quality of an MCP server is measured by how well it enables LLMs to accomplish real-world tasks.

**Framework context**: The `Tnzi.AI.Mcp` module already implements an MCP Server for the Tnzi framework. This skill guides the development of **standalone or external MCP servers** using the `ModelContextProtocol` .NET SDK.

---

## Process

### Phase 1: Research and Planning

#### 1.1 Understand MCP Design Principles

**API Coverage vs. Workflow Tools:**
Balance comprehensive API endpoint coverage with specialized workflow tools. Workflow tools can be more convenient for specific tasks, while comprehensive coverage gives agents flexibility to compose operations. When uncertain, prioritize comprehensive API coverage.

**Tool Naming and Discoverability:**
Clear, descriptive tool names help agents find the right tools quickly. Use consistent prefixes (e.g., `github_create_issue`, `github_list_repos`) and action-oriented naming.

**Context Management:**
Agents benefit from concise tool descriptions and the ability to filter/paginate results. Design tools that return focused, relevant data.

**Actionable Error Messages:**
Error messages should guide agents toward solutions with specific suggestions and next steps.

#### 1.2 Study MCP Protocol

**Navigate the MCP specification:**
- Start with: `https://modelcontextprotocol.io/sitemap.xml`
- Fetch specific pages with `.md` suffix for markdown format
- Key topics: specification overview, transport mechanisms (streamable HTTP, stdio), tool/resource/prompt definitions

#### 1.3 Study the .NET SDK

**NuGet Package**: `ModelContextProtocol`

```csharp
// NuGet: ModelContextProtocol
// NuGet: ModelContextProtocol.AspNetCore (for HTTP transport)
```

**Key SDK types:**
- `McpServer` / `McpServerTool` — server and tool registration
- `IMcpServer` — server interface for DI
- `McpServerOptions` — server configuration
- `McpToolAttribute` / `McpToolParameterAttribute` — tool metadata

#### 1.4 Plan Implementation

1. Review the target service's API documentation
2. Identify key endpoints and authentication requirements
3. Prioritize comprehensive API coverage — list endpoints to implement
4. Define input/output schemas for each tool

---

### Phase 2: Implementation

#### 2.1 Project Structure

```
MyMcpServer/
├── MyMcpServer.csproj
├── Program.cs                    # Server entry point
├── Tools/                        # Tool implementations
│   ├── ResourceTools.cs          # Resource-related tools
│   └── ActionTools.cs            # Action-related tools
├── Services/                     # Business logic
│   └── ApiClient.cs              # API client wrapper
├── Models/                       # Request/response models
│   └── ApiModels.cs
└── appsettings.json              # Configuration
```

#### 2.2 Server Setup (stdio transport)

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "my-mcp-server",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();  // 自动发现 [McpServerTool] 标记的工具

// 注册业务服务
builder.Services.AddHttpClient<IApiClient, ApiClient>();

await builder.Build().RunAsync();
```

#### 2.3 Server Setup (HTTP/SSE transport)

```csharp
using ModelContextProtocol;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "my-mcp-server",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddHttpClient<IApiClient, ApiClient>();

var app = builder.Build();
app.MapMcp();  // 映射 MCP HTTP 端点
await app.RunAsync();
```

#### 2.4 Implement Tools

**Basic Tool:**

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

[McpServerTool, Description("Search for repositories by name or topic")]
public static class SearchReposTool
{
    [McpToolMethod]
    public static async Task<string> ExecuteAsync(
        IMcpServer server,
        [Description("Search query string"), Required] string query,
        [Description("Maximum results to return (1-100, default 10)")] int maxResults = 10,
        [Description("Sort by: stars, forks, updated")] string sort = "stars")
    {
        // 验证输入
        if (string.IsNullOrWhiteSpace(query))
            return FormatError("Query parameter is required. Provide a search term.");

        maxResults = Math.Clamp(maxResults, 1, 100);

        var client = server.Services!.GetRequiredService<IApiClient>();
        var results = await client.SearchReposAsync(query, maxResults, sort);

        return FormatResults(results);
    }

    private static string FormatResults(IEnumerable<Repo> repos)
    {
        var sb = new StringBuilder();
        foreach (var repo in repos)
        {
            sb.AppendLine($"**{repo.FullName}** ({repo.Stars} stars)");
            sb.AppendLine($"  {repo.Description}");
            sb.AppendLine($"  URL: {repo.Url}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatError(string message)
        => $"Error: {message}";
}
```

**Tool with Structured Output:**

```csharp
[McpServerTool, Description("Get detailed information about a specific repository")]
public static class GetRepoTool
{
    [McpToolMethod]
    public static async Task<string> ExecuteAsync(
        IMcpServer server,
        [Description("Repository full name (e.g., 'owner/repo')"), Required] string fullName)
    {
        if (!fullName.Contains('/'))
            return "Error: Repository name must be in 'owner/repo' format. Example: 'microsoft/dotnet'";

        var client = server.Services!.GetRequiredService<IApiClient>();

        try
        {
            var repo = await client.GetRepoAsync(fullName);
            return JsonSerializer.Serialize(repo, JsonOptions);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return $"Error: Repository '{fullName}' not found. Check the name and try again.";
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
```

**Tool with Pagination:**

```csharp
[McpServerTool, Description("List issues for a repository with pagination")]
public static class ListIssuesTool
{
    [McpToolMethod]
    public static async Task<string> ExecuteAsync(
        IMcpServer server,
        [Description("Repository full name (e.g., 'owner/repo')"), Required] string repo,
        [Description("Filter by state: open, closed, all")] string state = "open",
        [Description("Page number (1-based)")] int page = 1,
        [Description("Items per page (1-100)")] int perPage = 20)
    {
        page = Math.Max(1, page);
        perPage = Math.Clamp(perPage, 1, 100);

        var client = server.Services!.GetRequiredService<IApiClient>();
        var (issues, totalCount) = await client.ListIssuesAsync(repo, state, page, perPage);

        var sb = new StringBuilder();
        sb.AppendLine($"Issues for {repo} ({state}) — Page {page}, showing {issues.Count} of {totalCount}");
        sb.AppendLine();

        foreach (var issue in issues)
        {
            sb.AppendLine($"#{issue.Number}: {issue.Title}");
            sb.AppendLine($"  State: {issue.State} | Labels: {string.Join(", ", issue.Labels)}");
            sb.AppendLine();
        }

        if (page * perPage < totalCount)
            sb.AppendLine($"More results available. Use page={page + 1} to see next page.");

        return sb.ToString();
    }
}
```

#### 2.5 Tool Annotations

Annotate tools with behavioral hints:

```csharp
[McpServerTool(
    ReadOnlyHint = true,      // 只读操作（搜索、查询）
    DestructiveHint = false,  // 不会删除或修改数据
    IdempotentHint = true,    // 多次调用结果相同
    OpenWorldHint = true)]    // 可能与外部网络交互
[Description("Search for repositories")]
public static class SearchReposTool { /* ... */ }
```

| Annotation | When `true` |
|-----------|-------------|
| `ReadOnlyHint` | Tool only reads data, no side effects |
| `DestructiveHint` | Tool can delete or permanently modify data |
| `IdempotentHint` | Multiple identical calls produce same result |
| `OpenWorldHint` | Tool interacts with external systems/networks |

---

### Phase 3: Quality and Testing

#### 3.1 Code Quality Checklist

- [ ] No duplicated code across tools (DRY)
- [ ] Consistent error handling with actionable messages
- [ ] All parameters have `[Description]` attributes
- [ ] Required parameters marked with `[Required]`
- [ ] Input validation at the start of every tool
- [ ] Pagination support for list operations
- [ ] Proper `using`/`IDisposable` patterns
- [ ] No hardcoded secrets or credentials

#### 3.2 Build and Test

```bash
# 编译验证
dotnet build

# 运行 MCP Inspector（SDK 自带调试工具）
# 从项目目录运行
npx @modelcontextprotocol/inspector dotnet run
```

#### 3.3 Error Handling Patterns

```csharp
// 标准错误格式 — 提供可操作的建议
private static string FormatError(string message, string? suggestion = null)
{
    var result = $"Error: {message}";
    if (suggestion != null)
        result += $"\nSuggestion: {suggestion}";
    return result;
}

// 用法示例
return FormatError(
    "Rate limit exceeded. Too many requests in the last minute.",
    "Wait 60 seconds and try again, or reduce the number of concurrent requests.");

return FormatError(
    $"Repository '{repo}' not found.",
    "Check the repository name format (should be 'owner/repo') and verify it exists.");
```

#### 3.4 Security Guidelines

- **Never hardcode API keys** — use environment variables or configuration
- **Validate all inputs** — check types, ranges, formats before processing
- **Sanitize outputs** — don't expose internal error details or stack traces
- **Rate limiting** — implement rate limiting for external API calls
- **Least privilege** — request only necessary API scopes/permissions
- **Log access** — record tool invocations for audit

```csharp
// 配置示例：从环境变量读取密钥
builder.Services.Configure<ApiOptions>(options =>
{
    options.ApiKey = builder.Configuration["API_KEY"]
        ?? throw new InvalidOperationException("API_KEY environment variable is required.");
    options.BaseUrl = builder.Configuration["API_BASE_URL"]
        ?? "https://api.example.com";
});
```

---

### Phase 4: Transport Selection

| Transport | Use Case | Notes |
|-----------|----------|-------|
| **stdio** | Local tools, CLI integration | Simplest; process-per-session |
| **Streamable HTTP** | Remote servers, multi-user | Scalable; stateless JSON recommended |

**Recommendation**: Use **stdio** for local development tools. Use **HTTP** for shared services that multiple agents or users need to access.

---

## Best Practices

### Tool Design

1. **One tool, one purpose** — avoid "god tools" that do everything
2. **Descriptive names** — `create_issue` not `do_action`; use consistent prefixes per service
3. **Concise descriptions** — explain what the tool does, not implementation details
4. **Parameter descriptions** — include format examples (e.g., "Repository full name, format: owner/repo")
5. **Reasonable defaults** — provide sensible defaults for optional parameters
6. **Focused responses** — return relevant data, not entire API responses

### Response Formatting

- **Use Markdown** for human-readable responses (lists, bold headers, links)
- **Use JSON** for structured data that agents will process programmatically
- **Include metadata** — total counts, pagination info, timestamps
- **Truncate large results** — limit response size, offer pagination

### API Client Best Practices

```csharp
// 使用 HttpClientFactory 管理 HttpClient 生命周期
builder.Services.AddHttpClient<IApiClient, ApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MyMcpServer/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// API 客户端实现
public class ApiClient : IApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = Check.NotNull(httpClient);
    }

    public async Task<IReadOnlyList<Repo>> SearchReposAsync(
        string query, int maxResults, string sort)
    {
        var response = await _httpClient.GetAsync(
            $"/search?q={Uri.EscapeDataString(query)}&limit={maxResults}&sort={sort}");

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<List<Repo>>() ?? [];
    }
}
```

---

## Reference

- **MCP Specification**: `https://modelcontextprotocol.io/specification/draft`
- **ModelContextProtocol NuGet**: `https://www.nuget.org/packages/ModelContextProtocol`
- **.NET SDK README**: `https://github.com/modelcontextprotocol/csharp-sdk`
- **MCP Inspector**: `npx @modelcontextprotocol/inspector`
- **Tnzi.AI.Mcp module**: Reference implementation in `src/Tnzi.AI.Mcp/`
