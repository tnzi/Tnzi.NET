namespace Tnzi.MultiTenancy;

/// <summary>
/// 租户有效性检查器（可选）
/// </summary>
public interface ITenantChecker
{
    /// <summary>
    /// 检查租户是否有效且可用
    /// </summary>
    Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
