namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 出票方（本公司）身份解析：System General 抬头 + Finance 支票签名配置 → <see cref="CheckIssuerInfo"/>
/// </summary>
/// <remarks>
/// 支票必须印出票公司抬头与签名，而这两者的真值源不同：
/// <list type="bullet">
/// <item>公司抬头/地址/联系方式/logo 是**全系统身份**，属 System General（<c>System:*</c>）；</item>
/// <item>授权签名（图片/姓名/职务）是**支票专属**，属 <see cref="FinanceOptions"/> 的 Checks 组。</item>
/// </list>
/// Finance 核心刻意不引用 <c>Tnzi.System</c>（同它不引用 AI/Storage 的理由），故经 <see cref="IConfiguration"/>
/// 读 <c>System:*</c>——System 模块把 <c>Sys_Setting</c> 接入了配置热链，读 IConfiguration 即拿到
/// 配置中心的运行时覆盖值，与直接消费 <c>ApplicationOptions</c> 等价。
/// <see cref="FinanceOptions.CheckIssuerName"/> / <see cref="FinanceOptions.CheckIssuerAddress"/> 非空时覆盖系统值
/// （多法人主体部署下支票抬头可能异于站点公司名）。
///
/// 经 DI 注入 <c>CheckService</c> 的公共构造函数，故为 public（同 <c>LedgerPostingEngine</c> 等协作类，
/// 见 <c>src/Tnzi.Finance/CLAUDE.md</c>「Internal 边界」）。
/// </remarks>
public class CheckIssuerResolver
{
    /// <summary>System General 配置节（<c>Tnzi.System</c> 的 ApplicationOptions 绑定于此）。</summary>
    private const string SystemSection = "System";

    private static readonly string[] LineSeparators = { "\r\n", "\r", "\n" };

    private readonly IConfiguration _configuration;
    private readonly IOptionsSnapshot<FinanceOptions> _options;

    public CheckIssuerResolver(IConfiguration configuration, IOptionsSnapshot<FinanceOptions> options)
    {
        _configuration = Check.NotNull(configuration);
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// 解析当前生效的出票方身份。全部字段可空——未配置时渲染器降级为不打抬头/签名，不报错。
    /// </summary>
    public CheckIssuerInfo Resolve()
    {
        var finance = _options.Value;
        var system = _configuration.GetSection(SystemSection);

        var address = Trim(finance.CheckIssuerAddress) ?? Trim(system["Address"]);

        return new CheckIssuerInfo
        {
            Name = Trim(finance.CheckIssuerName) ?? Trim(system["CompanyName"]) ?? Trim(system["SiteName"]),
            AddressLines = SplitLines(address),
            Phone = Trim(system["Phone"]),
            Email = Trim(system["Email"]),
            WebsiteUrl = Trim(system["WebsiteUrl"]),
            LogoUrl = Trim(system["LogoUrl"]),
            SignatureImageUrl = Trim(finance.CheckSignatureImageUrl),
            SignatureName = Trim(finance.CheckSignatureName),
            SignatureTitle = Trim(finance.CheckSignatureTitle)
        };
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> SplitLines(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
