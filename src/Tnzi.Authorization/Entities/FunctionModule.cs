namespace Tnzi.Authorization.Entities;

/// <summary>
/// 功能模块实体
/// </summary>
public class FunctionModule : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 模块名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 模块代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 模块描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 获取或设置 排序号
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// True when this row is owned by code (an
    /// <see cref="Tnzi.Security.Authorization.IPermissionDefinitionProvider"/> registered it
    /// at startup). System-managed rows are protected from rename/delete
    /// through the admin UI - only the declaring module can change them.
    /// </summary>
    /// <remarks>
    /// User-created rows have this flag <c>false</c>. Admin can freely edit
    /// and delete those. The <c>Auth_FunctionModule.IsSystemManaged</c>
    /// column is indexed via the regular EF Core column behaviour; no
    /// dedicated index is needed because every admin query is already
    /// filtered by name/code or id.
    /// </remarks>
    public bool IsSystemManaged { get; set; }

    /// <summary>
    /// Transient (not persisted) flag stamped on the admin read path: <c>true</c>
    /// when this module belongs to the FRAMEWORK built-in catalogue (its code
    /// matches a loaded <c>Tnzi.*</c> module), <c>false</c> for a consumer
    /// application's own modules. Lets the role-permission matrix list the
    /// consumer's own permissions first and separate the built-in catalogue.
    /// Resolved from the running module graph (see
    /// <see cref="Permissions.FrameworkModuleResolver"/>); default <c>false</c>
    /// so unstamped read paths simply treat everything as consumer-level.
    /// </summary>
    [NotMapped]
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 获取或设置 父模块ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 获取或设置 父模块
    /// </summary>
    public virtual FunctionModule? Parent { get; set; }

    /// <summary>
    /// 获取或设置 子模块集合
    /// </summary>
    public virtual ICollection<FunctionModule> Children { get; set; } = new List<FunctionModule>();

    /// <summary>
    /// 获取或设置 功能集合
    /// </summary>
    public virtual ICollection<ModuleFunction> Functions { get; set; } = new List<ModuleFunction>();
}
