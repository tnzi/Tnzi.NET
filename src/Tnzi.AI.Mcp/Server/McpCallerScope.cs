namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// 一次 MCP 请求的调用面：静态 API Key = 不受限；运行范围凭据 = 只许凭据点名的工具，agent 在凭据的租户下运行。
/// </summary>
/// <remarks>
/// 由 <see cref="McpServerHttpSecurityMiddleware"/> 在认证后存入
/// <c>HttpContext.Items[<see cref="McpServerSecurityMiddleware.CallerScopeItemKey"/>]</c>，
/// <see cref="McpServerHost"/> 据此过滤 <c>tools/list</c>、拒绝越界的 <c>tools/call</c>。
/// 没有 HTTP 上下文（进程内直接调用）时视为不受限 —— 那里没有外部调用方。
/// </remarks>
public sealed class McpCallerScope
{
    /// <summary>静态 API Key / 无认证：全部工具可见可调，不切换租户。</summary>
    public static McpCallerScope Unrestricted { get; } = new(null);

    /// <summary>以一枚运行范围凭据构造调用面。</summary>
    public McpCallerScope(RunScopedCredential? credential)
    {
        Credential = credential;
    }

    /// <summary>运行范围凭据；静态 key 时为 null。</summary>
    public RunScopedCredential? Credential { get; }

    /// <summary>是否为运行范围调用方。</summary>
    public bool IsRunScoped => Credential is not null;

    /// <summary>调用方的有效租户：工具在它之下列出与执行。运行范围凭据 = 签发时运行记录的租户；静态 key 时为 null（宿主）。</summary>
    public Guid? TenantId => Credential?.TenantId;

    /// <summary>持凭据的那次运行；静态 key 时为 null。</summary>
    public Guid? RunId => Credential?.RunId;

    /// <summary>持凭据的那次运行执行的 Agent；静态 key 时为 null。</summary>
    public Guid? AgentId => Credential?.AgentId;

    /// <summary>
    /// 派发那次运行的用户（<see cref="RunScopedCredential.CreatorId"/>）；静态 key 与无主运行时为 null。
    /// 它说明「这次运行替谁在干活」，供工具做按用户的授权，不代表调用方拥有那个用户的权限。
    /// </summary>
    public Guid? CreatorId => Credential?.CreatorId;

    /// <summary>
    /// 那次运行所在的对话线程（<see cref="RunScopedCredential.ThreadId"/>）；静态 key、一次性任务时为 null。
    /// 消费方工具据此知道这次调用是为哪一段对话做的，不必再拿 <see cref="RunId"/> 回查运行。
    /// </summary>
    public Guid? ThreadId => Credential?.ThreadId;

    /// <summary>
    /// 是否转发派发者自己连接的远程 MCP 服务器的工具（凭据带 <see cref="RunScopedCredential.UserMcpTools"/> 且运行有派发者）。
    /// 静态 API Key / 进程内调用方没有「派发者」，恒为 false。
    /// </summary>
    public bool AllowsUserMcpTools => Credential?.AllowsUserMcpTools == true;

    /// <summary>该调用面是否允许看见 / 调用名为 <paramref name="toolName"/> 的工具。</summary>
    public bool AllowsTool(string toolName) => Credential is null || Credential.AllowsTool(toolName);
}
