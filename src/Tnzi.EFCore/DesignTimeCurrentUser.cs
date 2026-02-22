
namespace Tnzi.EFCore;

/// <summary>
/// 设计时临时用户实现，用于 EF Core 迁移命令
/// </summary>
public class DesignTimeCurrentUser : ICurrentUser
{
    /// <summary>
    /// 获取用户ID（设计时返回 null）
    /// </summary>
    public Guid? Id => null;

    /// <summary>
    /// 获取用户名（设计时返回 "DesignTime"）
    /// </summary>
    public string? UserName => "DesignTime";

    /// <summary>
    /// 获取租户ID（设计时返回 null）
    /// </summary>
    public Guid? TenantId => null;

    /// <summary>
    /// 获取是否已认证（设计时返回 false）
    /// </summary>
    public bool IsAuthenticated => false;

    /// <summary>
    /// 获取用户角色列表（设计时返回空数组）
    /// </summary>
    public string[] Roles => Array.Empty<string>();

    /// <summary>
    /// 检查用户是否在指定角色中（设计时返回 false）
    /// </summary>
    public bool IsInRole(string roleName) => false;
}