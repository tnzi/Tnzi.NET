namespace Tnzi.Settings;

/// <summary>
/// 前端 origin 的唯一解析入口：主键 <c>System:FrontendUrl</c>，兼容旧键 <c>App:FrontendUrl</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★ 框架里凡是要拼一条指向前端的链接（邀请接受页、密码重置页、OAuth 回调的 postMessage origin、
/// returnUrl 白名单回退、邮件里的返回地址）都从这里取值，<b>不要再直接读配置键</b>。
/// 此前 Hosting 的邮件处理器经 <c>ApplicationOptions</c> 读 <c>System:FrontendUrl</c>，
/// Identity 的五处直接读 <c>App:FrontendUrl</c>：消费方只配一个键就以为全配了，
/// 密码重置信正常而邀请信的接受链接是相对路径、拒发，管理端仍报「已发出」。
/// </para>
/// <para>
/// 主键选 <c>System:FrontendUrl</c>：它是 <c>Tnzi.System</c> 里带校验器、可经设置中心热改的
/// <c>ApplicationOptions.FrontendUrl</c>（设置中心把库里的值投影回 <c>IConfiguration</c>，
/// 所以这里按字符串键读到的就是运行期生效值）；<c>App:FrontendUrl</c> 从来没有对应的 Options 类。
/// 本类只认字符串键、住在核心：Identity 不依赖 <c>Tnzi.System</c>，读不到那个类型。
/// </para>
/// <para>
/// 失败方向：两个键都没配返回 null，由调用方决定退路（拒发 / 只放行相对路径 / 用 <c>window.location.origin</c>）；
/// 只配了旧键照常工作但记一条 Warning 指名主键 —— 兼容不是静默的。
/// </para>
/// </remarks>
public static class FrontendUrlResolver
{
    /// <summary>主键：<c>ApplicationOptions.FrontendUrl</c>（section <c>System</c>）。</summary>
    public const string PrimaryKey = "System:FrontendUrl";

    /// <summary>旧键，仅为兼容保留；解析到它时记 Warning。</summary>
    public const string LegacyKey = "App:FrontendUrl";

    /// <summary>守卫消息里引用的键名说明，主键在前。</summary>
    public const string KeysForMessages = PrimaryKey + " (or the legacy " + LegacyKey + ")";

    /// <summary>
    /// 解析前端 origin：<see cref="PrimaryKey"/> 优先，空则退回 <see cref="LegacyKey"/>（记 Warning），都空返回 null。
    /// 返回值已去掉末尾的 <c>/</c>。
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
            return primary.Trim().TrimEnd('/');
        }

        var legacy = configuration[LegacyKey];
        if (string.IsNullOrWhiteSpace(legacy))
        {
            return null;
        }

        logger?.LogWarning(
            "Frontend URL was read from the legacy key {LegacyKey}; move it to {PrimaryKey} (ApplicationOptions.FrontendUrl, also editable in the settings centre). The legacy key keeps working but is not the documented one.",
            LegacyKey, PrimaryKey);
        return legacy.Trim().TrimEnd('/');
    }
}
