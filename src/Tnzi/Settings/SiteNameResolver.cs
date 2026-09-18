namespace Tnzi.Settings;

/// <summary>
/// 站点名的唯一解析入口：主键 <c>System:SiteName</c>，兼容旧键 <c>App:SiteName</c>。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="FrontendUrlResolver"/> 同形同因：前端 origin 收口那天，Identity 的两处事件
/// （<c>PasswordResetRequestedEvent.SiteName</c> / <c>UserInvitedEvent.SiteName</c>）仍从没有 Options 类的
/// <c>App:SiteName</c> 取站点名，而参考消费方只配 <c>System</c> 节 —— 消费方自己订阅这两个事件时拿到的站点名是空的。
/// 主键是 <c>Tnzi.System</c> 的 <c>ApplicationOptions.SiteName</c>（设置中心可热改、投影回 <c>IConfiguration</c>）；
/// 本类只认字符串键、住在核心，理由与前端 origin 那条相同。
/// </para>
/// <para>
/// 失败方向：两个键都没配返回 null（事件字段可空，处理器另有 <c>ISettingService.GetAppNameAsync</c> 兜底）；
/// 只配了旧键照常工作但记一条 Warning 指名主键。
/// </para>
/// </remarks>
public static class SiteNameResolver
{
    /// <summary>主键：<c>ApplicationOptions.SiteName</c>（section <c>System</c>）。</summary>
    public const string PrimaryKey = "System:SiteName";

    /// <summary>旧键，仅为兼容保留；解析到它时记 Warning。</summary>
    public const string LegacyKey = "App:SiteName";

    /// <summary>
    /// 解析站点名：<see cref="PrimaryKey"/> 优先，空则退回 <see cref="LegacyKey"/>（记 Warning），都空返回 null。
    /// 返回值已去掉首尾空白。
    /// </summary>
    public static string? Resolve(IConfiguration? configuration, ILogger? logger = null)
    {
        if (configuration == null)
        {
            return null;
        }

        var primary = configuration[PrimaryKey];
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        var legacy = configuration[LegacyKey];
        if (string.IsNullOrWhiteSpace(legacy))
        {
            return null;
        }

        logger?.LogWarning(
            "Site name was read from the legacy key {LegacyKey}; move it to {PrimaryKey} (ApplicationOptions.SiteName, also editable in the settings centre). The legacy key keeps working but is not the documented one.",
            LegacyKey, PrimaryKey);
        return legacy.Trim();
    }
}
