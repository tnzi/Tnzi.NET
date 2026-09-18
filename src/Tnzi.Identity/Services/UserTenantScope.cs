namespace Tnzi.Identity.Services;

/// <summary>
/// 多租户开启时，用户管理动作能碰到哪些账号。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b><see cref="User"/> 刻意不实现 <c>IMultiTenant</c>，所以没有全局查询过滤器替它把关。</b>
/// 理由有两条，都是登录链路的：登录时租户上下文尚未建立，按用户名找人必须跨租户；
/// 框架的租户过滤器是严格等值（没有 <c>TenantId IS NULL</c> 的共享子句），
/// <c>TenantId = null</c> 的全局账号一旦被过滤，在任何租户上下文里都会消失。
/// 于是它与同模块的 <c>Role</c> / <c>UserDetail</c>（都被过滤）不对称 ——
/// 而此前<b>没有任何补偿性校验</b>：租户 A 的管理员凭框架自带的 <c>user.*</c> 权限码
/// 能列出全部租户的用户，能重置、停用、删除别家租户的账号，全部 200，审计里是一次正常的管理操作。
/// </para>
/// <para>
/// 本类型就是那道补偿：<b>当前租户非空时，只有同租户的账号在范围内；本人恒在范围内</b>
/// （自助端点走同一批服务方法，而 <c>DefaultTenantId</c> 会把一个全局账号放进某个租户上下文里，
/// 不能让他连自己的资料都读不到）。当前租户为空（未配 <c>DefaultTenantId</c> 的全局管理员）
/// 或多租户未开启时不裁剪，与此前逐字相同。
/// </para>
/// <para>
/// ★ 越界一律按 <b>404</b> 拒绝，不是 403：403 会告诉对方「这个 id 存在，只是不归你」，
/// 那正是跨租户枚举需要的一位信息。
/// </para>
/// <para>
/// 租户的来源与 <c>UserService</c> 给新用户赋 <c>TenantId</c> 的来源一致
/// （<see cref="ICurrentTenant"/> 优先，其次 <see cref="ICurrentUser.TenantId"/>），
/// 这样「你建得出来的用户」与「你看得见的用户」是同一批。
/// </para>
/// <para>
/// ★ <b>没有已认证主体时不裁剪。</b>范围回答的是「这个主体能碰到谁」；没有主体的调用是框架自己在办事 ——
/// 登录链路上的多端登录顶替与登出、后台任务、事件处理器。那些调用与管理端共用同一批服务方法
/// （会话 / 登录日志 / 登录安全），而登录请求里的租户上下文来自 header 或 <c>DefaultTenantId</c>，
/// 与正在登录的人未必同租户：按它裁剪，顶替会静默失效，账号却照常登进来。
/// 管理端与自助端点都在认证之后，这条规则碰不到它们。
/// </para>
/// </remarks>
public readonly struct UserTenantScope
{
    /// <summary>不裁剪：多租户未开启，或当前没有租户上下文。</summary>
    public static readonly UserTenantScope Unrestricted = default;

    private UserTenantScope(Guid? tenantId, Guid? selfUserId)
    {
        TenantId = tenantId;
        SelfUserId = selfUserId;
    }

    /// <summary>裁剪到的租户；<c>null</c> 表示不裁剪。</summary>
    public Guid? TenantId { get; }

    /// <summary>当前用户本人，恒在范围内。</summary>
    public Guid? SelfUserId { get; }

    /// <summary>是否不裁剪。</summary>
    public bool IsUnrestricted => TenantId == null;

    /// <summary>按当前环境解析范围。</summary>
    public static UserTenantScope Resolve(bool multiTenancyEnabled, ICurrentTenant? currentTenant, ICurrentUser? currentUser)
    {
        if (!multiTenancyEnabled || currentUser?.IsAuthenticated != true)
        {
            return Unrestricted;
        }

        var tenantId = currentTenant?.Id ?? currentUser?.TenantId;
        return tenantId.HasValue ? new UserTenantScope(tenantId, currentUser?.Id) : Unrestricted;
    }

    /// <summary>某个已加载的账号是否在范围内。</summary>
    public bool Contains(User user)
    {
        Check.NotNull(user);
        return Contains(user.Id, user.TenantId);
    }

    /// <summary>按 id 与租户判断是否在范围内。</summary>
    public bool Contains(Guid userId, Guid? userTenantId)
        => TenantId == null || userTenantId == TenantId || userId == SelfUserId;

    /// <summary>把范围作为谓词加到查询上。</summary>
    public IQueryable<User> Apply(IQueryable<User> query)
    {
        Check.NotNull(query);
        if (TenantId == null)
        {
            return query;
        }

        var tenantId = TenantId.Value;
        if (SelfUserId is { } self)
        {
            return query.Where(u => u.TenantId == tenantId || u.Id == self);
        }

        return query.Where(u => u.TenantId == tenantId);
    }
}
