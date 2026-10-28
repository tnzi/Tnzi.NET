namespace Tnzi.Payment.Billing.Options;

/// <summary>
/// 发票与收据上的卖方资料（<c>Payment:Invoice:Seller</c>）。
/// </summary>
public class InvoiceSellerOptions
{
    /// <summary>标志图片的最大字节数：标志随快照嵌进每一张票据，大于这个数就不嵌（记一条告警）。</summary>
    public const int MaxLogoBytes = 512 * 1024;

    /// <summary>默认模板标题的出厂强调色（未配置 <see cref="AccentColor"/> 时）。</summary>
    public const string DefaultAccentColor = "#4f46e5";

    /// <summary>卖方名称（公司法定名称）。为空时退回 <c>Payment:Invoice:CompanyName</c>。</summary>
    public string? Name { get; set; }

    /// <summary>地址，一行一项（街道、城市 省 邮编、国家）。</summary>
    public List<string> AddressLines { get; set; } = [];

    /// <summary>电话。</summary>
    public string? Phone { get; set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; set; }

    /// <summary>网站。</summary>
    public string? Website { get; set; }

    /// <summary>
    /// 标志文件的路径（绝对路径，或相对应用目录）。与 <see cref="LogoFileId"/> 二选一，都配时后者优先。
    /// 支持 png / jpg / gif / svg / webp。
    /// </summary>
    public string? LogoPath { get; set; }

    /// <summary>标志在 Storage 里的文件 Id（需要加载 Storage 模块）。</summary>
    public Guid? LogoFileId { get; set; }

    /// <summary>
    /// 税务登记号，每项一个标签加号码（例如 <c>GST/HST</c> + <c>123456789 RT0001</c>、<c>QST</c> + <c>1234567890 TQ0001</c>）。
    /// 为空时退回 <c>Payment:Invoice:TaxId</c>（标签 Tax ID）。
    /// </summary>
    public List<InvoiceTaxRegistration> TaxRegistrations { get; set; } = [];

    /// <summary>
    /// 三份默认模板（发票、收据、贷项通知单）标题的强调色，十六进制 <c>#RRGGBB</c> 或 <c>#RGB</c>（例如 <c>#0B5FFF</c>）。
    /// 为空时用 <see cref="DefaultAccentColor"/>。与其余卖方资料一起进开票时的快照。
    /// </summary>
    public string? AccentColor { get; set; }

    /// <summary>
    /// <paramref name="value"/> 是否是 <c>#RRGGBB</c> / <c>#RGB</c>。颜色要写进模板的样式表，只认这两种形态：
    /// 任何别的字符都可能结束这条声明、改写别的样式。
    /// </summary>
    public static bool IsHexColor(string? value)
        => value is { Length: 4 or 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
}

/// <summary>一个税务登记号。</summary>
public class InvoiceTaxRegistration
{
    /// <summary>标签（GST/HST、QST、VAT）。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>号码。</summary>
    public string Number { get; set; } = string.Empty;
}
