namespace Tnzi.Security.Authorization;

/// <summary>
/// 权限检查器接口
/// </summary>
public interface IPermissionChecker
{
    /// <summary>
    /// 检查当前用户是否有权限
    /// </summary>
    Task<bool> IsGrantedAsync(string permissionName);

    /// <summary>
    /// 检查指定用户是否有权限
    /// </summary>
    Task<bool> IsGrantedAsync(Guid userId, string permissionName);

    /// <summary>
    /// 检查当前用户是否有任意一个权限
    /// </summary>
    Task<bool> IsGrantedAnyAsync(params string[] permissionNames);

    /// <summary>
    /// 检查当前用户是否有全部权限
    /// </summary>
    Task<bool> IsGrantedAllAsync(params string[] permissionNames);

    /// <summary>
    /// 检查权限，无权限则抛异常
    /// </summary>
    Task CheckAsync(string permissionName);
}

