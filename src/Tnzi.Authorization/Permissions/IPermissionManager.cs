namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 权限管理器接口
/// </summary>
public interface IPermissionManager
{
    /// <summary>
    /// 获取权限定义
    /// </summary>
    Task<PermissionDefinition?> GetAsync(string name);

    /// <summary>
    /// 获取所有权限
    /// </summary>
    Task<List<PermissionDefinition>> GetAllAsync();

    /// <summary>
    /// 获取权限组定义
    /// </summary>
    Task<PermissionGroupDefinition?> GetGroupAsync(string name);

    /// <summary>
    /// 获取所有权限组
    /// </summary>
    Task<List<PermissionGroupDefinition>> GetAllGroupsAsync();

    /// <summary>
    /// 检查当前用户是否有权限
    /// </summary>
    Task<bool> IsGrantedAsync(string permissionName);

    /// <summary>
    /// 检查指定用户是否有权限
    /// </summary>
    Task<bool> IsGrantedAsync(Guid userId, string permissionName);
    
    /// <summary>
    /// 刷新权限定义
    /// 当权限配置发生变更时调用此方法重新加载
    /// </summary>
    Task RefreshAsync();
}

