namespace Tnzi.AI.Services;

/// <summary>
/// 一次外部执行的<b>运行范围</b>凭据。
/// </summary>
/// <remarks>
/// 它不是「某个用户」的身份，而是「某一次运行」的身份：随运行结束失效，权限上限是该 Agent
/// 自身的权限 —— <b>绝不是</b>派发这次运行的那个人的完整权限。差别在于，外部 agent 能执行
/// 任意代码，把派发者的权限交给它等于把那个人的账号交出去。
/// </remarks>
public sealed record RunScopedCredential
{
    /// <summary>运行 ID。</summary>
    public required Guid RunId { get; init; }

    /// <summary>该运行执行的 Agent。回写权限的上限由它决定。</summary>
    public required Guid AgentId { get; init; }

    /// <summary>租户。</summary>
    public Guid? TenantId { get; init; }
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
