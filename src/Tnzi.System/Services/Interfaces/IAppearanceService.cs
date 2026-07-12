namespace Tnzi.System.Services;

/// <summary>
/// 全局外观服务：管理端主题快照的读取与维护。
/// 超级管理员（或持 system.appearance.update 者）保存的主题对全体已登录用户生效。
/// </summary>
public interface IAppearanceService
{
    /// <summary>
    /// 获取全局管理端主题（未配置时 Theme 为 null，客户端回退本地默认值）。
    /// </summary>
    Task<Result<AdminThemeDto>> GetAdminThemeAsync();

    /// <summary>
    /// 保存全局管理端主题快照（覆盖式整体保存）。
    /// </summary>
    Task<Result<AdminThemeDto>> SaveAdminThemeAsync(SaveAdminThemeDto input);

    /// <summary>
    /// 清除全局管理端主题（所有客户端回退本地默认值）。幂等。
    /// </summary>
    Task<Result> ResetAdminThemeAsync();
}
