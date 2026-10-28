namespace Tnzi.Storage.Workspace.Entities;

/// <summary>
/// 管理员给某个用户设的存储配额，覆盖 <c>Storage:Quota:DefaultUserAllowanceBytes</c>。没有这一行的用户用默认值。
/// </summary>
/// <remarks>
/// 类名刻意不叫 <c>UserQuota</c>：名字太泛，全模块图一起加载时实体自动发现按类名注册，同名类型会撞上。
/// </remarks>
public class StorageAllowance : AuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>用户 ID。</summary>
    public Guid UserId { get; set; }

    /// <summary>这个用户可用的字节数；<c>null</c> = 不限（即便默认值有上限）。</summary>
    public long? AllowanceBytes { get; set; }

    /// <summary>备注（为什么给他这么多），只给管理员看。</summary>
    public string? Note { get; set; }
}
