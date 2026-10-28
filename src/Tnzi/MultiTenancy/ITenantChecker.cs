namespace Tnzi.MultiTenancy;

/// <summary>
/// 租户有效性检查器（可选）
/// </summary>
/// <remarks>
/// 实现可以是 Scoped（通常要查租户表，背后是 DbContext），所以调用方必须按请求解析它：
/// 单例服务或约定式中间件经 <c>HttpContext.RequestServices</c> 或新建的作用域取，绝不构造注入。
/// </remarks>
public interface ITenantChecker
{
    /// <summary>
    /// 检查租户是否有效且可用
    /// </summary>
    Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
