namespace Tnzi.AI.Services;

/// <summary>
/// 一次外部执行的<b>运行范围</b>凭据。
/// </summary>
/// <remarks>
/// <para>
/// 它不是「某个用户」的身份，而是「某一次运行」的身份：随运行结束失效，能碰的工具面由
/// <see cref="AllowedToolNames"/> 显式给定 —— <b>绝不是</b>派发这次运行的那个人的完整权限。
/// 差别在于，外部 agent 能执行任意代码，把派发者的权限交给它等于把那个人的账号交出去。
/// </para>
/// <para>
/// ★ 调用面必须由凭据<b>自带</b>而不是由消费方「记得去查」：此前凭据校验完就被丢掉，
/// MCP server 对它与一把静态 API Key 一视同仁，契约里写的「上限是该 Agent 自身的权限」没有任何一处在执行。
/// </para>
/// </remarks>
public sealed record RunScopedCredential
{
    /// <summary>通配：允许调用 MCP server 暴露的全部工具。</summary>
    public const string AllTools = "*";

    /// <summary>运行 ID。</summary>
    public required Guid RunId { get; init; }

    /// <summary>该运行执行的 Agent。</summary>
    public required Guid AgentId { get; init; }

    /// <summary>租户：凭据持有者在 MCP server 上的执行租户上下文（可信来源是签发时的运行记录，不是请求头）。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// 允许经此凭据调用的 MCP 工具名（大小写不敏感）。<c>["*"]</c> = 全部；空列表 = 只能 <c>tools/list</c>（结果为空）、
    /// 一次 <c>tools/call</c> 都不许 —— 缺省是关闭的那一边。
    /// </summary>
    public required IReadOnlyList<string> AllowedToolNames { get; init; }

    /// <summary>该凭据是否允许调用名为 <paramref name="toolName"/> 的工具。</summary>
    public bool AllowsTool(string toolName)
        => AllowedToolNames.Any(n => n == AllTools || string.Equals(n, toolName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 校验运行范围凭据。
/// </summary>
/// <remarks>
/// <para>
/// 契约放在 <b>AI 核心</b>，实现在 <c>Tnzi.AI.Cli</c>，消费方是 <c>Tnzi.AI.Mcp</c> ——
/// 两个可选子模块因此互不引用，各自单独加载都成立。
/// </para>
/// <para>
/// 未注册任何实现时，回写通道就不存在：MCP server 只认它自己配置的静态 API key。
/// 这是<b>正确的默认</b> —— 没装外部执行能力就不该多出一条认证路径。
/// </para>
/// </remarks>
public interface IRunScopedCredentialValidator
{
    /// <summary>
    /// 校验一个 token。无效、过期、或对应运行已结束时返回 <c>null</c>。
    /// </summary>
    Task<RunScopedCredential?> ValidateAsync(string token, CancellationToken cancellationToken = default);
}
