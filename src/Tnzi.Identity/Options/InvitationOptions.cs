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
    /// <para>
    /// 取值范围 1 到 <see cref="MaxLinkLifetimeHours"/>（30 天），超出则启动校验失败；
    /// 管理员发邀请 / 重发时指定的有效期受同一个范围约束（超出答 400）。
    /// </para>
    /// </remarks>
    public int LinkLifetimeHours { get; set; } = 168;

    /// <summary>
    /// 邀请链接有效期的上限：720 小时（30 天，与 Auth0 允许的上限相同）。
    /// </summary>
    /// <remarks>
    /// 接受邀请是匿名的：一条有效期以年计的链接就是一份以年计的凭据，而且没有人会记得它还在。
    /// </remarks>
    public const int MaxLinkLifetimeHours = 720;

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
    /// 留空时框架退回 <c>{System:FrontendUrl}/admin/accept-invitation?token=</c>（origin 经 <c>FrontendUrlResolver</c>），
    /// 即 <c>@tnzi/ui-admin</c> 的接受页在默认 <c>basePath</c>（<c>/admin</c>）下的地址。
    /// </para>
    /// <para>
    /// ★ 以下情形必须配它，否则邀请信照发、链接却打不开：管理台的 <c>basePath</c> 不是 <c>/admin</c>
    /// （例如 <c>/</c>，接受页就在 <c>{origin}/accept-invitation</c>）；管理台与 <c>System:FrontendUrl</c> 不同源；
    /// 接受页由应用自己提供（移动端深链同理）。
    /// </para>
    /// <para>
    /// 真正需要自定义时，实现 <see cref="Services.IInvitationUrlGenerator"/> 覆盖整段逻辑；
    /// 这个配置项只是为了让最常见的那种情况不必写代码。
    /// </para>
    /// </remarks>
    public string? AcceptUrlTemplate { get; set; }
}
