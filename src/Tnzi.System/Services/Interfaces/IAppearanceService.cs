namespace Tnzi.System.Services;

/// <summary>
/// 全局外观服务：按 <c>scope</c> 保管前端主题快照。
/// <para>
/// 超级管理员（或持 <c>system.appearance.update</c> 者）保存的快照对该 scope 下
/// 全体用户生效。
/// </para>
/// <para>
/// <b>scope 存在的理由</b>：一个部署可能同时跑管理端与对话端两个前端产品，它们的外壳
/// 字段完全不同（侧栏宽度 / Tab 栏 vs 会话列宽 / 气泡圆角）。快照对后端是不透明 JSON，
/// 所以不需要为每个产品各建一套端点与服务方法，只需按 scope 分行存放。
/// 约定：<c>admin</c> = 管理端，<c>chat</c> = 对话端；应用可自取其它名字。
/// </para>
/// </summary>
public interface IAppearanceService
{
    /// <summary>管理端 scope 的约定名。</summary>
    public const string AdminScope = "admin";

    /// <summary>
    /// 获取某个 scope 的全局主题（未配置时 Theme 为 null，客户端回退本地默认值）。
    /// </summary>
    Task<Result<ThemeSnapshotDto>> GetThemeAsync(string scope);

    /// <summary>
    /// 保存某个 scope 的全局主题快照（覆盖式整体保存）。
    /// </summary>
    Task<Result<ThemeSnapshotDto>> SaveThemeAsync(string scope, SaveThemeSnapshotDto input);

    /// <summary>
    /// 清除某个 scope 的全局主题（该 scope 的客户端回退本地默认值）。幂等。
    /// </summary>
    Task<Result> ResetThemeAsync(string scope);

    /// <summary>
    /// 获取全局管理端主题。等价于 <c>GetThemeAsync(AdminScope)</c>。
    /// </summary>
    Task<Result<ThemeSnapshotDto>> GetAdminThemeAsync() => GetThemeAsync(AdminScope);

    /// <summary>
    /// 保存全局管理端主题。等价于 <c>SaveThemeAsync(AdminScope, input)</c>。
    /// </summary>
    Task<Result<ThemeSnapshotDto>> SaveAdminThemeAsync(SaveThemeSnapshotDto input)
        => SaveThemeAsync(AdminScope, input);

    /// <summary>
    /// 清除全局管理端主题。等价于 <c>ResetThemeAsync(AdminScope)</c>。
    /// </summary>
    Task<Result> ResetAdminThemeAsync() => ResetThemeAsync(AdminScope);
}
