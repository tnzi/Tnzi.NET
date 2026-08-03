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

    /// <summary>
    /// 当前用户是否为超级管理员（绕过一切权限检查）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 权威判定，等价于 <c>IFunctionAuthorizationService.IsSuperAdminAsync(CurrentUser.Id)</c>，
    /// 放在这里只是省掉「为了判一次超管而多注入一个服务、再自己取一次 CurrentUser.Id」的样板：
    /// <see cref="Tnzi.Application.ApplicationService"/> 本来就有 <c>PermissionChecker</c> 懒属性。
    /// </para>
    /// <para>
    /// <b>不要用角色名字符串自己判。</b> 超管角色名是部署配置
    /// （<c>Authorization:SuperAdminRoles</c>，未配置时约定为 <c>SuperAdmin</c>），
    /// 硬编码 <c>IsInRole("admin")</c> 这类判断会在角色改名时静默失效，
    /// 也会把恰好同名的业务角色误当成超管。
    /// </para>
    /// <para>默认实现返回 false：没有超管概念的实现把所有用户当普通用户。</para>
    /// </remarks>
    Task<bool> IsCurrentUserSuperAdminAsync() => Task.FromResult(false);
}

