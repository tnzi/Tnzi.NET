namespace Tnzi.Authorization.Dtos;

/// <summary>
/// 设置用户直授功能请求（覆盖原有直授集）
/// </summary>
public class SetUserFunctionsRequest
{
    /// <summary>
    /// 功能ID列表
    /// </summary>
    public IEnumerable<Guid> FunctionIds { get; set; } = null!;
}

/// <summary>
/// 在给定切片内设置用户直授/否定集的请求（只覆盖切片内，切片外原样保留）
/// </summary>
/// <remarks>
/// 供"只掌握功能目录一个子集"的消费方使用（例如只渲染自己那几个 <c>xxx.*</c>
/// 码的权限矩阵）：<see cref="ScopeFunctionIds"/> 声明本次写入允许触碰的边界，
/// 边界外的直授行由服务端保证不受影响。<see cref="FunctionIds"/> 必须是
/// <see cref="ScopeFunctionIds"/> 的子集，否则 400。
/// </remarks>
public class SetUserFunctionsInScopeRequest
{
    /// <summary>
    /// 切片：本次写入允许触碰的功能ID全集
    /// </summary>
    public IEnumerable<Guid> ScopeFunctionIds { get; set; } = null!;

    /// <summary>
    /// 切片内的新集合（必须是切片的子集）
    /// </summary>
    public IEnumerable<Guid> FunctionIds { get; set; } = null!;
}

/// <summary>
/// 一个账号的权限全景：目录、角色基线、用户级覆盖、最终生效集，一次读出。
/// </summary>
/// <remarks>
/// <para>
/// 管理员打开一个人的权限页带着的问题不是「他能做什么」，而是<b>「为什么，以及我该改哪一处」</b>。
/// 一张扁平的生效码列表回答不了：同一个码可能来自角色、来自单独授予，或者被单独否定。
/// 此前拿到这幅画面要四次往返（模块树、每个模块的功能、角色基线、用户覆盖）再在客户端拼，
/// 每个消费方各拼一遍，而拼错的方向是安静的：多显示一个「角色给的」勾，或者少显示一个。
/// </para>
/// <para>
/// 解析语义与运行时检查同源：<c>Effective = (RoleGranted ∪ Allowed) − Denied</c>；
/// 超管的 <c>Effective</c> 是整个（切片内的）目录，覆盖行对他没有效果，界面据
/// <see cref="IsSuperAdmin"/> 把矩阵改成只读说明而不是渲染一张不起作用的表。
/// </para>
/// <para>
/// <see cref="Scope"/> 是可选的码前缀（如 <c>catalog.</c>）：只掌握目录一个子集的消费方拿它把画面
/// 收窄到自己的码，与写侧的 <c>set-in-scope</c> / <c>set-denied-in-scope</c> 对称。
/// </para>
/// </remarks>
public class UserPermissionPictureDto
{
    public Guid UserId { get; set; }

    /// <summary>超管绕过每一项检查；为 true 时 <see cref="Effective"/> 等于目录，覆盖集仅供参考。</summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>本次应用的码前缀过滤；null 表示整个目录。</summary>
    public string? Scope { get; set; }

    /// <summary>账号持有的角色，每个角色带上它贡献的码。</summary>
    public List<UserPermissionPictureRoleDto> Roles { get; set; } = [];

    /// <summary>（切片内）生效的功能目录，按模块序、功能序。</summary>
    public List<UserPermissionPictureItemDto> Catalogue { get; set; } = [];

    /// <summary>经任一角色获得的码（基线）。</summary>
    public List<string> RoleGranted { get; set; } = [];

    /// <summary>用户级单独授予的码。</summary>
    public List<string> Allowed { get; set; } = [];

    /// <summary>用户级单独否定的码（无论哪个角色给过都不生效）。</summary>
    public List<string> Denied { get; set; } = [];

    /// <summary>最终生效的码。</summary>
    public List<string> Effective { get; set; } = [];
}

/// <summary>权限全景里的一个角色。</summary>
public class UserPermissionPictureRoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>这个角色贡献的（切片内的）码。</summary>
    public List<string> Granted { get; set; } = [];
}

/// <summary>权限全景里的一个目录项。</summary>
public class UserPermissionPictureItemDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid ModuleId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public PermissionCategory Category { get; set; }
}
