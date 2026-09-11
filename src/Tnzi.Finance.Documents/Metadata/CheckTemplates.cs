namespace Tnzi.Finance.Documents.Metadata;

/// <summary>
/// 内置支票模板的存储坐标（<c>Tnzi.Template</c> 中的 Module / Category / Name）
/// </summary>
/// <remarks>
/// 消费应用把 <c>BankAccount.CheckTemplateName</c> 设为 <see cref="Cpa006Canada"/>
/// 或自建模板名即可切换版式；留空则回退 <see cref="DefaultName"/>。
/// </remarks>
public static class CheckTemplates
{
    /// <summary>模板所属模块（与 Finance 核心同名，管理端按此归类）</summary>
    public const string Module = "Tnzi.Finance";

    /// <summary>模板分类</summary>
    public const string Category = "Check";

    /// <summary>加拿大 CPA Standard 006 商用支票（支票在上 + 两联存根）</summary>
    public const string Cpa006Canada = "check-cpa006-ca";

    /// <summary>加拿大 CPA Standard 006 开窗信封版（§5.4.1 Figure C）</summary>
    public const string Cpa006CanadaWindow = "check-cpa006-ca-window";

    /// <summary>美式凭证式，支票在上（3.5 / 3.5 / 4 in）</summary>
    public const string VoucherTopUs = "check-voucher-top-us";

    /// <summary>美式凭证式，支票在中（3.5 / 3.5 / 4 in）</summary>
    public const string VoucherMiddleUs = "check-voucher-middle-us";

    /// <summary>美式凭证式，支票在下（4 / 3.5 / 3.5 in）</summary>
    public const string VoucherBottomUs = "check-voucher-bottom-us";

    /// <summary>每页三张支票（3 × 8.5 × 3.5 in + 0.5 in 走纸边）</summary>
    public const string ThreePerPage = "check-3up";

    /// <summary><c>BankAccount.CheckTemplateName</c> 为空时使用的模板</summary>
    public const string DefaultName = Cpa006Canada;

    /// <summary>
    /// <c>BankAccount.CheckTemplateName</c> 为空时，按账户版式选出的出厂默认模板。
    /// </summary>
    /// <remarks>
    /// ★ 这就是 <see cref="CheckLayout"/> 在模板驱动（HTML）渲染路径上<b>生效</b>的地方。
    /// 在此之前该枚举只被 PdfSharp 备选渲染器读，默认路径整个忽略它 ——
    /// 管理端能选 <c>ThreePerPage</c>、选了没反应、也不报错。
    /// 版式的权威表达仍是模板：一旦档案或请求指定了模板名，就以模板为准，本方法不参与。
    /// </remarks>
    public static string DefaultForLayout(CheckLayout layout)
        => layout == CheckLayout.ThreePerPage ? ThreePerPage : DefaultName;

    /// <summary>模板描述（播种时写入，管理端列表可见）</summary>
    public const string Cpa006CanadaDescription =
        "Canadian business cheque compliant with CPA Standard 006: cheque on top plus two voucher stubs, "
        + "millimetre-positioned, pre-printed-stock aware (noprint elements), courtesy amount box and MICR band.";
}
