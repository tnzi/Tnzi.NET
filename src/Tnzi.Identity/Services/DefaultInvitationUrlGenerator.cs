namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IInvitationUrlGenerator"/>
/// <remarks>
/// 按 <c>Identity:Invitation:AcceptUrlTemplate</c> 拼；没配就退回
/// <c>App:FrontendUrl</c> + <c>/accept-invitation?token=</c>。
/// 两者都没有时给一条相对路径 —— 那样至少邮件模板里还看得出该往哪去，
/// 比拼出 <c>https:///accept-invitation</c> 这种废链接强。
/// </remarks>
public class DefaultInvitationUrlGenerator : IInvitationUrlGenerator
{
    private const string DefaultPath = "/accept-invitation";

    private readonly IOptionsMonitor<IdentityOptions> _options;
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// 初始化一个 <see cref="DefaultInvitationUrlGenerator"/> 类型的新实例。
    /// </summary>
    public DefaultInvitationUrlGenerator(IOptionsMonitor<IdentityOptions> options, IConfiguration? configuration = null)
    {
        _options = Check.NotNull(options);
        _configuration = configuration;
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

        var frontendUrl = _configuration?["App:FrontendUrl"];
        return string.IsNullOrWhiteSpace(frontendUrl)
            ? $"{DefaultPath}?token={encoded}"
            : $"{frontendUrl.TrimEnd('/')}{DefaultPath}?token={encoded}";
    }
}
