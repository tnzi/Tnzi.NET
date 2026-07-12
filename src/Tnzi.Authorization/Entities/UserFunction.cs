namespace Tnzi.Authorization.Entities;

/// <summary>
/// 用户功能实体（用户与功能的直接授权/否定关联，不经角色）
/// </summary>
/// <remarks>
/// 权限解析：用户有效权限 = (角色授权 ∪ 用户直授) − 用户拒绝（用户级
/// 优先，超管短路不受影响）。allow 行承载"给单个用户单独多开权限"，
/// deny 行承载"唯独这个用户不行"；唯一索引 (UserId, FunctionId) 保证
/// 同一功能对同一用户只有一行——allow 与 deny 互斥，写路径后写者赢。
/// </remarks>
public class UserFunction : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置 功能ID
    /// </summary>
    public Guid FunctionId { get; set; }

    /// <summary>
    /// 获取或设置 功能
    /// </summary>
    public virtual ModuleFunction Function { get; set; } = null!;

    /// <summary>
    /// 获取或设置 是否授予。true = 允许（allow 直授）；false = 拒绝
    /// （deny，解析时从并集中扣除该码，无论哪个角色授予过）。
    /// 与 <see cref="IsEnabled"/> 语义分离：IsEnabled 是行开关，IsGranted 是授权方向。
    /// </summary>
    public bool IsGranted { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
