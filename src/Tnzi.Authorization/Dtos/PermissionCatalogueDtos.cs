namespace Tnzi.Authorization.Dtos;

/// <summary>
/// 权限目录里的一个模块，连同它的全部功能。
/// </summary>
/// <remarks>
/// <para>
/// 平铺：层级只由 <see cref="ParentId"/> 表达，前端按它在客户端建树（同 <c>GET admin/modules</c>）。
/// 管理面口径：停用与退役的模块和功能都在里面，带着 <see cref="IsEnabled"/> / <see cref="IsRetired"/>
/// 由页面自己决定怎么呈现（分配矩阵隐藏退役功能，权限目录页置灰显示）。
/// </para>
/// <para>
/// ★ 它存在的理由是门禁：角色授权、用户直授与权限目录三张管理页都要读目录，而目录原本只挂在
/// <c>admin/modules</c>（类级 <c>authorization.functionModule.view</c>）下。只被授予页面自身码的委托管理员
/// 打开页面就是 403 加一张空矩阵。三个控制器各自在自己的类级门下提供同一份目录读取。
/// </para>
/// </remarks>
public class PermissionCatalogueModuleDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int Order { get; set; }

    public bool IsEnabled { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>由某个 <c>IPermissionDefinitionProvider</c> 在代码里声明（编码、名称与上级只读）。</summary>
    public bool IsSystemManaged { get; set; }

    /// <summary>已加载的模块里没有任何一个还声明它。</summary>
    public bool IsRetired { get; set; }

    /// <summary>
    /// 属于框架内置目录（编码与一个已加载的 <c>Tnzi.*</c> 业务模块对应）。分配矩阵据此把应用自己的权限排在前面。
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>该模块的全部功能，按 <c>Order</c> 排序。</summary>
    /// <summary>
    /// Platform or application group. Present only when the super-admin bypass is narrowed
    /// to platform codes (<c>Authorization:SuperAdminBypassScope = Platform</c>); a module no
    /// provider declares (admin-created) is <see cref="PermissionScope.Application"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PermissionScope? Scope { get; set; }

    public List<ModuleFunctionDto> Functions { get; set; } = [];
}
