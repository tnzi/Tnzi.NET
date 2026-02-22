namespace Tnzi.MultiTenancy;

/// <summary>
/// 当前租户服务接口
/// </summary>
public interface ICurrentTenant
{
    /// <summary>
    /// 当前租户ID
    /// </summary>
    Guid? Id { get; }

    /// <summary>
    /// 当前租户名称
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// 当前租户是否可用
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 切换租户上下文（返回IDisposable，using语句自动恢复）
    /// </summary>
    IDisposable Change(Guid? tenantId, string? tenantName = null);
}

