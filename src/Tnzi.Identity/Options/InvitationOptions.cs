namespace Tnzi.Identity.Options;

/// <summary>
/// 邀请注册配置。配置路径 <c>Identity:Invitation</c>。
/// </summary>
public class InvitationOptions
{
    /// <summary>
    /// 邀请链接有效期（小时）。默认 <b>168 小时 = 7 天</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 7 天是这一类流程的行业共识（AWS IAM Identity Center、Auth0 组织邀请的默认值、
    /// Microsoft Entra、Okta 都是这个量级；Auth0 允许的上限是 30 天）。
    /// </para>
    /// <para>
    /// ★★ <strong>不要照搬「魔法链接」的 10 到 15 分钟。</strong>那条建议针对的是
    /// <b>登录凭据</b>：本人正坐在屏幕前、几秒内就会点开。邀请是<b>入职流程</b> ——
    /// 邮件可能周五下班后发出、下周一才被读到，卡在 IT 交接、卡在新人还没拿到电脑。
    /// 把它压到分钟级，唯一的效果是每个人都要让管理员重发一次，
    /// 而重发本身也是走邮件的，安全性一点没变。
    /// </para>
    /// </remarks>
    public int LinkLifetimeHours { get; set; } = 168;

    /// <summary>
    /// 接受邀请成功后是否直接签发登录令牌（免去再登录一次）。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 关掉它则接受成功后返回不带令牌的结果，由前端引导用户去登录页。
    /// 需要接受与首次登录在两台设备上完成时（比如接受在手机上、工作在电脑上），
    /// 关掉更符合预期。<b>无论开关如何，签发都要过登录守卫</b>
    /// （<see cref="Services.LoginMethod.Invitation"/>）。
    /// </remarks>
    public bool SignInAfterAccept { get; set; } = true;

    /// <summary>
    /// 前端接受邀请页的地址模板，<c>{token}</c> 会被替换为邀请令牌。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 例：<c>https://admin.example.com/accept-invitation?token={token}</c>。
    /// 留空时框架退回按 <c>App:FrontendUrl</c> 拼一条默认路径。
    /// </para>
    /// <para>
    /// 真正需要自定义时，实现 <see cref="Services.IInvitationUrlGenerator"/> 覆盖整段逻辑；
    /// 这个配置项只是为了让最常见的那种情况不必写代码。
    /// </para>
    /// </remarks>
    public string? AcceptUrlTemplate { get; set; }
}
