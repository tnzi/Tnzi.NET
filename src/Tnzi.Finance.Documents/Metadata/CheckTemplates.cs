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

    /// <summary><c>BankAccount.CheckTemplateName</c> 为空时使用的模板</summary>
    public const string DefaultName = Cpa006Canada;

    /// <summary>模板描述（播种时写入，管理端列表可见）</summary>
    public const string Cpa006CanadaDescription =
        "Canadian business cheque compliant with CPA Standard 006: cheque on top plus two voucher stubs, "
        + "millimetre-positioned, pre-printed-stock aware (noprint elements), courtesy amount box and MICR band.";
}
