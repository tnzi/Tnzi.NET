namespace Tnzi.Authorization.DataAuth.Entities;

/// <summary>
/// 实体角色实体（用于数据授权）
/// </summary>
public class EntityRole : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 实体信息ID
    /// </summary>
    public Guid EntityInfoId { get; set; }

    /// <summary>
    /// 获取或设置 实体信息
    /// </summary>
    public virtual EntityInfo EntityInfo { get; set; } = null!;

    /// <summary>
    /// 获取或设置 角色ID
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// 获取或设置 数据权限操作类型（查询、更新、删除等）
    /// </summary>
    public DataAuthOperation Operation { get; set; }

    /// <summary>
    /// <see cref="Filter"/> 列的长度上限。保存期按它 400，别指望数据库：宽松模式的 MySQL 会把超长串
    /// 静默截断成半截 JSON，那半截在查询时是 deny-all（零行、一条 Warning），与「配错了」无法区分。
    /// </summary>
    public const int FilterMaxLength = 2000;

    /// <summary>
    /// 获取或设置 过滤条件（<see cref="FilterGroup"/> 的 JSON，方言见 <see cref="FilterGroupJson"/>；
    /// 留空 = 不设限）
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 数据权限操作类型
/// </summary>
public enum DataAuthOperation
{
    /// <summary>
    /// 查询
    /// </summary>
    Query = 1,

    /// <summary>
    /// 更新
    /// </summary>
    Update = 2,

    /// <summary>
    /// 删除
    /// </summary>
    Delete = 4,

    /// <summary>
    /// 全部操作
    /// </summary>
    All = Query | Update | Delete
}

