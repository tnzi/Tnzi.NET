namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IInvitationUrlGenerator"/>
/// <remarks>
/// 按 <c>Identity:Invitation:AcceptUrlTemplate</c> 拼；没配就退回
/// 前端 origin（<see cref="FrontendUrlResolver"/>：<c>System:FrontendUrl</c>，旧键 <c>App:FrontendUrl</c>）
/// + <c>/accept-invitation?token=</c>。
/// 两者都没有时给一条相对路径 —— 它只到得了 <c>POST /admin/invitations</c> 响应里的 <c>acceptUrl</c>
/// （邀请人自己拿去拼），比 <c>https:///accept-invitation</c> 这种废链接强；
/// ★ 但<b>邮件不会带着它发出去</b>：<c>Tnzi.Hosting</c> 的 <c>UserInvitedEventHandler</c> 对非绝对地址拒发并抛异常，
/// 没有任何邮件客户端打得开 <c>/accept-invitation?token=</c>。要让邀请邮件真的发出去，两个键至少配一个。
/// </remarks>
public class DefaultInvitationUrlGenerator : IInvitationUrlGenerator
{
    private const string DefaultPath = "/accept-invitation";

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
