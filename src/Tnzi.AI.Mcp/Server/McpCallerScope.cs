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

    /// <summary>agent 调用应在其下运行的租户；静态 key 时为 null（根作用域，行为与以前一致）。</summary>
    public Guid? TenantId => Credential?.TenantId;

    /// <summary>该调用面是否允许看见 / 调用名为 <paramref name="toolName"/> 的工具。</summary>
    public bool AllowsTool(string toolName) => Credential is null || Credential.AllowsTool(toolName);
}
