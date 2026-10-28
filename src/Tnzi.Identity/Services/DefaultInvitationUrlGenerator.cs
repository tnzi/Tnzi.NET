namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IInvitationUrlGenerator"/>
/// <remarks>
/// <para>
/// 按 <c>Identity:Invitation:AcceptUrlTemplate</c> 拼；没配就退回
/// 前端 origin（<see cref="FrontendUrlResolver"/>：<c>System:FrontendUrl</c>，旧键 <c>App:FrontendUrl</c>）
/// + <c>/admin/accept-invitation?token=</c>。
/// 两者都没有时给一条相对路径 —— 它只到得了 <c>POST /admin/invitations</c> 响应里的 <c>acceptUrl</c>
/// （邀请人自己拿去拼），比 <c>https:///admin/accept-invitation</c> 这种废链接强；
/// ★ 但<b>邮件不会带着它发出去</b>：<c>Tnzi.Hosting</c> 的 <c>UserInvitedEventHandler</c> 对非绝对地址拒发并抛异常，
/// 没有任何邮件客户端打得开 <c>/admin/accept-invitation?token=</c>。要让邀请邮件真的发出去，两个键至少配一个。
/// </para>
/// <para>
/// ★★ 默认路径对准的是框架自带的接受页：它在 <c>@tnzi/ui-admin</c> 里，而 ui-admin 默认把每一条路由挂在
/// <c>basePath</c> <c>/admin</c> 下。此前默认是 <c>{origin}/accept-invitation</c>，按框架自己的默认配置装出来的管理台
/// 没有这个地址：信照发、发信方一切正常，被邀请人点开是 404。管理台挂在别处（<c>basePath</c> 为 <c>/</c> 或 <c>/console</c>）、
/// 与 <c>System:FrontendUrl</c> 不同源，或者接受页由应用自己提供时，配 <c>AcceptUrlTemplate</c>。
/// </para>
/// </remarks>
public class DefaultInvitationUrlGenerator : IInvitationUrlGenerator
{
    /// <summary>
    /// <c>@tnzi/ui-admin</c> 的接受页在默认 <c>basePath</c>（<c>/admin</c>）下的路径。
    /// </summary>
    private const string DefaultPath = "/admin/accept-invitation";

    private readonly IOptionsMonitor<IdentityOptions> _options;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<DefaultInvitationUrlGenerator>? _logger;

    /// <summary>
    /// 初始化一个 <see cref="DefaultInvitationUrlGenerator"/> 类型的新实例。
    /// </summary>
    public DefaultInvitationUrlGenerator(
        IOptionsMonitor<IdentityOptions> options,
        IConfiguration? configuration = null,
        ILogger<DefaultInvitationUrlGenerator>? logger = null)
    {
        _options = Check.NotNull(options);
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public string GenerateUrl(User user, string token)
    {
        Check.NotNull(user);
        Check.NotNullOrWhiteSpace(token);

        var encoded = Uri.EscapeDataString(token);
        var template = _options.CurrentValue.Invitation.AcceptUrlTemplate;

        if (!string.IsNullOrWhiteSpace(template))
        {
            return template.Replace("{token}", encoded, StringComparison.OrdinalIgnoreCase);
        }

        var frontendUrl = FrontendUrlResolver.Resolve(_configuration, _logger);
        return string.IsNullOrWhiteSpace(frontendUrl)
            ? $"{DefaultPath}?token={encoded}"
            : $"{frontendUrl}{DefaultPath}?token={encoded}";
    }
}
