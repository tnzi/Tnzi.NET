namespace Tnzi.Authorization.DataAuth.Entities;

/// <summary>
/// 实体信息实体（用于数据授权）
/// </summary>
public class EntityInfo : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 实体名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 实体类型名称（CLR 全名 <c>typeof(T).FullName</c>，不带程序集；过滤路径按它等值查表）
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 实体显示名称
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 获取或设置 是否启用数据授权
    /// </summary>
    public bool IsDataAuthEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 实体角色集合。
    /// [JsonIgnore]:三个 EntityRole 读端点直接返回实体并 <c>Include(er =&gt; er.EntityInfo)</c>,查询带跟踪,
    /// EF fix-up 会同时填上 <see cref="EntityRole.EntityInfo"/> 与本集合,不忽略则序列化陷入
    /// EntityInfo↔EntityRoles 循环直接 500(与父模块 <c>FunctionModule.Parent</c> 同规)。
    /// 前端只读 <see cref="EntityRole.EntityInfoId"/>。
    /// </summary>
    [JsonIgnore]
    public virtual ICollection<EntityRole> EntityRoles { get; set; } = new List<EntityRole>();
}

