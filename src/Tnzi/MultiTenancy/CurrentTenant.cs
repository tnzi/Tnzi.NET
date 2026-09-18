namespace Tnzi.MultiTenancy;

/// <summary>
/// 当前租户服务实现
/// </summary>
/// <remarks>
/// <para>
/// ★ <see cref="Change"/> 的覆盖存放在<b>静态</b> <see cref="AsyncLocal{T}"/> 上，随 ExecutionContext 流动，
/// 与 <c>Tnzi.Data.AmbientUnitOfWork</c> 同形；本类的实例只是读写门面。
/// 服务是 Scoped 的，而框架有大量「在新建的作用域里再解析一个 <see cref="ICurrentTenant"/>」的路径
/// （<c>LocalEventBus</c> 给事件打租户、后台处理器、AI Channels 的网关等）——
/// 覆盖若存在实例字段上，新作用域里的新实例什么都看不见：凡由 <c>TenantResolverMiddleware</c>、
/// 按租户轮转的后台服务、登录注册流程经 <c>Change()</c> 建立的租户，事件与其处理器一律读空，
/// 处理器里的仓储按 null 过滤、写入挂错租户。只有 JWT claim 来源的租户能穿过去，
/// 因为它走的是 <c>IHttpContextAccessor</c> 那条静态 AsyncLocal。
/// </para>
/// <para>
/// AsyncLocal 只沿父流向子流传播：<c>Change()</c> 在 <c>using</c> 里成对使用，
/// 覆盖对 using 体内的全部 await / 新作用域 / <c>Task.Run</c> 可见，using 结束即恢复；
/// 两个并行分支各自 Change 互不可见。async 方法内部的 Set 不回传调用者流 ——
/// 所以 <see cref="Change"/> 与 Dispose 都是同步方法，这一点不能改。
/// </para>
/// </remarks>
public class CurrentTenant : ICurrentTenant
{
    private readonly ICurrentUser? _currentUser;
    private static readonly AsyncLocal<TenantOverride?> _tenantOverride = new();

    /// <summary>
    /// 初始化当前租户服务
    /// </summary>
    /// <param name="currentUser">当前用户服务</param>
    public CurrentTenant(ICurrentUser? currentUser = null)
    {
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public Guid? Id
    {
        get
        {
            // 优先使用临时覆盖的租户ID
            if (_tenantOverride.Value != null)
                return _tenantOverride.Value.TenantId;

            // 否则使用当前用户的租户ID
            return _currentUser?.TenantId;
        }
    }

    /// <inheritdoc />
    public string? Name
    {
        get
        {
            // 优先使用临时覆盖的租户名称
            if (_tenantOverride.Value != null)
                return _tenantOverride.Value.TenantName;

            // 否则返回null（需要从数据库查询）
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => Id.HasValue;

    /// <inheritdoc />
    public IDisposable Change(Guid? tenantId, string? tenantName = null)
    {
        var previousOverride = _tenantOverride.Value;
        _tenantOverride.Value = new TenantOverride(tenantId, tenantName);
        return new TenantChangeScope(() => _tenantOverride.Value = previousOverride);
    }

    /// <summary>
    /// 租户覆盖信息
    /// </summary>
    private class TenantOverride
    {
        public Guid? TenantId { get; }
        public string? TenantName { get; }

        public TenantOverride(Guid? tenantId, string? tenantName)
        {
            TenantId = tenantId;
            TenantName = tenantName;
        }
    }

    /// <summary>
    /// 租户切换范围
    /// </summary>
    private class TenantChangeScope : IDisposable
    {
        private readonly Action _disposeAction;

        public TenantChangeScope(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        public void Dispose()
        {
            _disposeAction();
        }
    }
}
