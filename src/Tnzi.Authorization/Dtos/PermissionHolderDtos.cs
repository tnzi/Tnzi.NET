namespace Tnzi.Authorization.Dtos;

/// <summary>
/// 一个权限码的持有者全景：哪些角色授予它、最终哪些人持有它以及经由什么、哪些人被单独否定。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="UserPermissionPictureDto"/> 是同一件事的两个方向：那边回答「这个人能做什么、为什么」，
/// 这边回答访问审查要问的「<b>谁能做这件事</b>」。审查者拿到的名单必须与运行时一致：
/// <see cref="Users"/> 恰好是调用者范围内、账号仍存在、<c>CheckPermissionAsync</c> 对这个码答 true 的那些人
/// （集成测试逐人比对）。
/// </para>
/// <para>
/// 最容易漏掉的人群是超管：配置在 <c>Authorization:SuperAdminRoles</c> 里的角色成员不经任何授权行就持有每一个码，
/// 只按授权行反查的名单里永远没有他们。这里他们以 <see cref="PermissionHolderUserDto.IsSuperAdmin"/> 出现，
/// 超管角色本身以 <see cref="PermissionRoleGrantState.SuperAdmin"/> 出现在 <see cref="Roles"/> 里。
/// </para>
/// <para>
/// 功能不生效（停用或退役）时，授权行一条都不起作用，持有者只剩超管，与运行时检查相同（超管在检查最前短路，
/// 连码是否存在都不看）。指向它的授权行仍列在 <see cref="Roles"/> 里并标为 <see cref="PermissionRoleGrantState.Dormant"/>：
/// 休眠授权在功能恢复时原样回来，藏起来等于让「重新启用一个码」成为一次没人审过的授权。
/// </para>
/// </remarks>
public class PermissionHoldersDto
{
    /// <summary>功能 Id。</summary>
    public Guid FunctionId { get; set; }

    /// <summary>权限码（按库里存的写法）。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>功能名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>分类（仅展示用）。</summary>
    public PermissionCategory Category { get; set; }

    /// <summary>管理员的启停开关。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>当前部署已不再声明这个码。</summary>
    public bool IsRetired { get; set; }

    /// <summary>功能此刻是否生效（启用且未退役）。为 false 时只有超管持有它。</summary>
    public bool IsInEffect { get; set; }

    /// <summary>
    /// 调用者没有 <c>user.view</c>，<see cref="Users"/> 与 <see cref="DeniedUsers"/> 只带 id、不带用户名。
    /// 名单本身照样完整：这个端点的门是 <c>authorization.permission.view</c>，成批的账号名属于用户管理面。
    /// </summary>
    public bool UserNamesHidden { get; set; }

    /// <summary>
    /// 与这个码有关的角色：超管角色、授予它的角色，以及授权行停用或休眠的角色。
    /// 顺序：超管角色、生效、休眠、行停用；同组内按名称。
    /// </summary>
    public List<PermissionHolderRoleDto> Roles { get; set; } = [];

    /// <summary>最终持有这个码的账号及其持有路径，按用户名排序。</summary>
    public List<PermissionHolderUserDto> Users { get; set; } = [];

    /// <summary>
    /// 对这个码有用户级否定行的账号（无论哪个角色给过都不生效）。超管不在其中：否定行对超管无效，
    /// 列出来会把一个仍然持有它的人读成被排除了。
    /// </summary>
    public List<PermissionDeniedUserDto> DeniedUsers { get; set; } = [];
}

/// <summary>一个角色与某个码的关系。</summary>
public enum PermissionRoleGrantState
{
    /// <summary>超管角色：成员绕过每一项检查，不需要、也不读授权行。</summary>
    SuperAdmin = 0,

    /// <summary>授权行启用、功能生效：成员此刻持有这个码（被单独否定的除外）。</summary>
    Effective = 1,

    /// <summary>授权行启用但功能已停用或退役（休眠授权）：此刻不起作用，功能恢复时原样回来。</summary>
    Dormant = 2,

    /// <summary>授权行存在但行开关关闭：不起作用。</summary>
    GrantDisabled = 3,
}

/// <summary><see cref="PermissionHoldersDto"/> 里的一个角色。</summary>
public class PermissionHolderRoleDto
{
    /// <summary>角色 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>角色名；角色已不存在时为空。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>这个角色与该码的关系。</summary>
    public PermissionRoleGrantState State { get; set; }

    /// <summary>调用者范围内、账号仍存在的成员数。</summary>
    public int MemberCount { get; set; }
}

/// <summary><see cref="PermissionHoldersDto"/> 里的一个持有者。</summary>
public class PermissionHolderUserDto
{
    /// <summary>用户 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>用户名；调用者没有 <c>user.view</c>、或 Identity 用户仓储不可用时为空。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>经超管角色持有（绕过一切检查，否定行对他无效）。</summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>经用户级直授持有。</summary>
    public bool IsDirect { get; set; }

    /// <summary>经由的角色（超管角色在前）。只经直授持有时为空。</summary>
    public List<PermissionHolderRoleRefDto> Roles { get; set; } = [];
}

/// <summary>持有路径上的一个角色。</summary>
public class PermissionHolderRoleRefDto
{
    /// <summary>角色 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>角色名。</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>对某个码被单独否定的账号。</summary>
public class PermissionDeniedUserDto
{
    /// <summary>用户 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>用户名；调用者没有 <c>user.view</c>、或 Identity 用户仓储不可用时为空。</summary>
    public string UserName { get; set; } = string.Empty;
}
