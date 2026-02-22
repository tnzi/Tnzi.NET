namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 权限定义提供者接口
/// </summary>
public interface IPermissionDefinitionProvider
{
    /// <summary>
    /// 定义权限
    /// </summary>
    void Define(IPermissionDefinitionContext context);
}

