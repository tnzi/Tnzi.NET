namespace Tnzi.AI.Options;

/// <summary>
/// MCP (Model Context Protocol) 配置选项
/// </summary>
/// <remarks>
/// <para>
/// MCP 由 Anthropic 提出，用于让 AI 与外部工具/数据源安全交互。
/// 框架通过 MCP 将工具暴露给 AI 模型。
/// </para>
/// <para>
/// 常见实现 MCP 的客户端如 Cursor、Claude Desktop 等。
/// </para>
/// </remarks>
public class McpOptions
{
    /// <summary>
    /// 是否启用 MCP
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// MCP 服务器配置列表
    /// </summary>
    public List<McpServerConfig> Servers { get; set; } = [];

    /// <summary>
    /// MCP 工具列表缓存秒数（默认 300 秒即 5 分钟，0 表示不缓存）
    /// </summary>
    public int ToolCacheSeconds { get; set; } = 300;
}

/// <summary>
/// MCP 服务器配置
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// 服务器显示名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 服务端点 URL
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// HTTP 请求头（如鉴权）
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>
    /// 连接类型
    /// </summary>
    public McpConnectionType ConnectionType { get; set; } = McpConnectionType.Stdio;

    /// <summary>
    /// 允许调用的工具列表（空表示全部允许）
    /// </summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>
    /// 审批模式
    /// </summary>
    public McpApprovalMode ApprovalMode { get; set; } = McpApprovalMode.AlwaysRequire;

    /// <summary>
    /// 是否在工具名前加 Server 名（避免多服务器时重名）
    /// </summary>
    public bool PrefixToolNameWithServer { get; set; } = false;

    /// <summary>
    /// 始终需审批的工具列表
    /// </summary>
    public List<string> AlwaysRequireApprovalTools { get; set; } = [];

    /// <summary>
    /// 从不需审批的工具列表
    /// </summary>
    public List<string> NeverRequireApprovalTools { get; set; } = [];

    /// <summary>
    /// 启动命令（Stdio 时）。Stdio 模式下通过 Command + Arguments 启动进程，Arguments[0] 为可执行路径时可省略 Command
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// 环境变量。Stdio 模式下当 ApplyEnvironmentToProcess 为 true 时传入子进程。
    /// 与 ModelContextProtocol SDK 的 StdioClientTransportOptions 一致，便于与官方 SDK 行为对齐。
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>
    /// 是否将 Environment 中的变量应用到当前进程后再启动子进程；为 true 时子进程继承这些变量
    /// </summary>
    public bool ApplyEnvironmentToProcess { get; set; } = false;

    /// <summary>
    /// 启动参数（Stdio 时）
    /// </summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>
    /// OAuth 客户端认证配置（用于连接需要 OAuth 的 MCP 服务器，如 GitHub MCP、Slack MCP）。
    /// 配置后自动通过 client_credentials 流获取访问令牌并注入 Authorization 头。
    /// </summary>
    public McpOAuthConfig? OAuth { get; set; }
}

/// <summary>
/// MCP 客户端 OAuth 配置（client_credentials 流）
/// </summary>
public class McpOAuthConfig
{
    /// <summary>
    /// OAuth Client ID
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth Client Secret（可选，公开客户端可省略）
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Token Endpoint URL
    /// </summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用 OIDC/OAuth metadata discovery。
    /// 启用后会优先从 MetadataUrl 或 AuthorizationServer 派生的 well-known 地址发现 token/revocation endpoint。
    /// </summary>
    public bool EnableMetadataDiscovery { get; set; }

    /// <summary>
    /// OAuth/OIDC metadata 文档地址（如 /.well-known/openid-configuration）。
    /// </summary>
    public string? MetadataUrl { get; set; }

    /// <summary>
    /// 授权服务器基地址。未显式提供 MetadataUrl 时，将自动拼接 well-known metadata 地址。
    /// </summary>
    public string? AuthorizationServer { get; set; }

    /// <summary>
    /// Revocation Endpoint URL。
    /// 未配置时 metadata discovery 成功后可自动填充。
    /// </summary>
    public string? RevocationEndpoint { get; set; }

    /// <summary>
    /// 请求的 OAuth Scopes
    /// </summary>
    public string[]? Scopes { get; set; }
}

/// <summary>
/// MCP 连接类型
/// </summary>
public enum McpConnectionType
{
    /// <summary>
    /// 标准输入/输出（本地进程）
    /// </summary>
    Stdio = 0,

    /// <summary>
    /// HTTP/SSE 连接
    /// </summary>
    Http = 1
}

/// <summary>
/// MCP 工具审批模式
/// </summary>
public enum McpApprovalMode
{
    /// <summary>
    /// 从不需审批
    /// </summary>
    NeverRequire = 0,

    /// <summary>
    /// 始终需审批
    /// </summary>
    AlwaysRequire = 1,

    /// <summary>
    /// 按工具配置
    /// </summary>
    Specific = 2
}
