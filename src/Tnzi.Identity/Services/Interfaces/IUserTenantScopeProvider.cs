namespace Tnzi.Identity.Services;

/// <summary>
/// 当前请求的 <see cref="UserTenantScope"/>，以及那些要查用户表才答得出的范围问题。
/// </summary>
/// <remarks>
/// <para>
/// <c>UserService</c> 自己持有用户实体，直接按 <see cref="UserTenantScope.Resolve"/> 现算。
/// 会话 / 登录日志 / 登录安全 / 邀请这几个服务拿到的只是一个用户 id，而它们的表
/// （<c>UserSession</c> / <c>LoginLog</c> / <c>AuthToken</c>）都没有 <c>TenantId</c>、都不是 <c>IMultiTenant</c>，
/// 全局过滤器管不到 —— 于是管理端按用户 id 读写之前要先问一句「这个人归我管吗」，
/// 列表与统计则要按用户表的租户裁剪。本契约把这两个问题收在一处，各服务不必各自复制
/// <c>ICurrentTenant</c> + <c>ICurrentUser</c> + <c>MultiTenancyOptions</c> 那三件套。
/// </para>
/// <para>
/// ★ 越界的回答一律与「不存在」相同（404 / 空），理由见 <see cref="UserTenantScope"/>。
/// </para>
/// </remarks>
public interface IUserTenantScopeProvider
{
    /// <summary>本次请求的范围。每次现算：租户上下文是 AsyncLocal 的，缓存会钉死切换前的值。</summary>
    UserTenantScope Current { get; }

    /// <summary>
    /// 目标账号是否在范围内。不裁剪时恒为 <c>true</c>（不查库）；裁剪时不存在的 id 视为不在范围内，
    /// 已软删的账号照常按租户判断（它的会话与登录记录仍归原租户的管理员管）。
    /// </summary>
    Task<bool> ContainsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 范围内账号的 id 子查询，给按 <c>UserId</c> 关联的表加谓词用（<c>ids.Contains(x.UserId)</c>，
    /// EF 翻译成 <c>IN (SELECT ...)</c>）；不裁剪时为 <c>null</c>，调用方不加谓词。
    /// </summary>
    IQueryable<Guid>? InScopeUserIds();

    /// <summary>
    /// 把一批用户 id 收窄到范围内的那些（去重；顺序不保证）。不裁剪时原样返回且不查库；
    /// 裁剪时一次 IN 查询，不存在的 id 与别家租户的 id 一起被省略。
    /// </summary>
    /// <remarks>
    /// 给「拿到一批 id、要对每个 id 回答点什么」的服务用（presence 批量解析）：按 id 逐个问
    /// <see cref="ContainsAsync"/> 是 N 次查询，而 <see cref="InScopeUserIds"/> 那种子查询形状只能拼进别的查询里。
    /// 越界的 id 是<b>省略</b>而不是标记：调用方据此分得清「不在目录里」与「离线 / 无数据」。
    /// </remarks>
    Task<IReadOnlyCollection<Guid>> FilterAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
