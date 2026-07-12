namespace Tnzi.Authorization.Entities;

/// <summary>
/// 模块功能实体
/// </summary>
public class ModuleFunction : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 功能名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 功能代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 功能描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 获取或设置 所属模块ID
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// 获取或设置 所属模块。
    /// [JsonIgnore]:模块树端点返回带 Functions 的实体,EF fix-up 会填充本
    /// 反向导航,不忽略则序列化陷入 Functions↔FunctionModule 循环直接 500。
    /// </summary>
    [JsonIgnore]
    public virtual FunctionModule FunctionModule { get; set; } = null!;

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 获取或设置 权限分类。纯展示元数据：Technical = 技术/运维面，
    /// 分配界面渲染警示徽标；不驱动任何隐式授权。既有数据迁移后为 Business。
    /// </summary>
    public PermissionCategory Category { get; set; } = PermissionCategory.Business;

    /// <summary>
    /// True when this row was seeded from an
    /// <see cref="Permissions.IPermissionDefinitionProvider"/>. Admin UI
    /// must show this as a read-only row and prevent code/delete edits —
    /// only re-seeding via code can change the contract.
    /// </summary>
    /// <remarks>
    /// The flag is *not* used by the permission check itself; it only
    /// gates admin-side mutations. <c>IsEnabled</c> remains
    /// admin-controllable on system-managed rows (deploy may need to
    /// temporarily disable a permission without redeploying code).
    /// </remarks>
    public bool IsSystemManaged { get; set; }
}

