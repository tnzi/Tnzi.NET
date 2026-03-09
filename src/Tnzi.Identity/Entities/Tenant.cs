namespace Tnzi.Identity.Entities;

/// <summary>
/// 租户主数据实体（全局共享，不参与多租户过滤）
/// </summary>
[Table("Tenant")]
public class Tenant : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 租户名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 租户编码（可选唯一）
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 过期时间（为空表示不过期）
    /// </summary>
    public DateTime? ExpiredAt { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }
}
