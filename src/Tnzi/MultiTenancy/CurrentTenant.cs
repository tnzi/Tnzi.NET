
namespace Tnzi.MultiTenancy;

/// <summary>
/// 当前租户服务实现
/// </summary>
public class CurrentTenant : ICurrentTenant
{
    private readonly ICurrentUser? _currentUser;
    private readonly AsyncLocal<TenantOverride?> _tenantOverride = new();

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