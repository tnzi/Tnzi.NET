namespace Tnzi.Hosting.Events.Handlers;

/// <summary>
/// 邮件处理器拼链接时读的两个应用 URL 的<b>真实</b>配置键。
/// </summary>
/// <remarks>
/// 处理器经 <see cref="ISettingService.GetApplicationOptions"/> 拿到 <see cref="ApplicationOptions"/>，
/// 而它的 section 是 <c>System</c>（<c>[ConfigSection("System")]</c>，经配置中心可热设置），
/// 所以键是 <c>System:ApiBaseUrl</c> / <c>System:FrontendUrl</c>。
/// ★ 09-04 与 09-12 两条失败关闭的守卫都把消息写成了并不存在的 <c>Application:*</c>：
/// 守卫本身是对的，但运维唯一看得到的出路指向一个绑定不到任何东西的键，
/// 照它加配置、重启，处理器继续抛同一条消息。这里从类型派生而不是手写，section 改名时消息跟着变。
/// </remarks>
internal static class ApplicationUrlSettingKeys
{
    private static readonly string Section = ConfigSectionResolver.Resolve(typeof(ApplicationOptions));

    public static readonly string ApiBaseUrl = $"{Section}:{nameof(ApplicationOptions.ApiBaseUrl)}";

    public static readonly string FrontendUrl = $"{Section}:{nameof(ApplicationOptions.FrontendUrl)}";
}
