namespace Tnzi.Finance.Documents.Metadata;

/// <summary>
/// 一条出厂支票版式的完整声明（模板正文的坐标 + 目录里的描述，同一处定义）
/// </summary>
/// <remarks>
/// <see cref="ResourceFile"/> 指向随程序集分发的嵌入资源（模板正文），其余字段是目录元数据。
/// 播种器、版式目录、渲染器的每页切页三处都读这一份声明 —— 出厂版式只声明一次，
/// 不存在「加了模板忘了加目录条目」或「目录说每页三张而模板画一张」这类漂移。
/// </remarks>
internal sealed record BuiltInCheckTemplate(
    string Name,
    string ResourceFile,
    string DisplayName,
    string Description,
    string? Region,
    string PaperSize,
    int ChecksPerPage,
    CheckPosition? Position,
    IReadOnlyList<CheckStockType> SupportedStockTypes);

/// <summary>
/// 框架出厂的支票版式清单（代码声明即出厂目录）
/// </summary>
/// <remarks>
/// 覆盖北美最通用的几种商用支票版式。全部为 Letter（215.9 × 279.4mm）绝对毫米定位，
/// 支票本体一律 8.5 × 3.5in（88.9mm），底部 15.9mm 为 MICR 净空带。
/// <para>
/// ★ <b>「预印 / 白纸」是票纸类型不是版式</b>：每一份模板都同时处理两种票纸
/// （预印元素带 <c>noprint</c>、白纸模式现打 MICR），因此不为它另造一套模板。
/// </para>
/// </remarks>
internal static class BuiltInCheckTemplates
{
    private static readonly IReadOnlyList<CheckStockType> BothStockTypes =
        new[] { CheckStockType.PrePrinted, CheckStockType.Blank };

    /// <summary>出厂版式（顺序即管理端选择器的默认排列：先加拿大、后美式、最后每页多张）。</summary>
    public static readonly IReadOnlyList<BuiltInCheckTemplate> All = new[]
    {
        new BuiltInCheckTemplate(
            CheckTemplates.Cpa006Canada,
            "check-cpa006-ca.cshtml",
            "Canada CPA-006 - cheque on top with two stubs",
            CheckTemplates.Cpa006CanadaDescription,
            Region: "CA", PaperSize: "Letter", ChecksPerPage: 1, Position: CheckPosition.Top,
            SupportedStockTypes: BothStockTypes),

        new BuiltInCheckTemplate(
            CheckTemplates.Cpa006CanadaWindow,
            "check-cpa006-ca-window.cshtml",
            "Canada CPA-006 - window envelope",
            "Canadian business cheque per CPA Standard 006 section 5.4.1 Figure C: the written and numeric "
            + "amounts share one line so the payee name and address block sits lower, aligned with a standard "
            + "window envelope. Cheque on top plus two voucher stubs.",
            Region: "CA", PaperSize: "Letter", ChecksPerPage: 1, Position: CheckPosition.Top,
            SupportedStockTypes: BothStockTypes),

        new BuiltInCheckTemplate(
            CheckTemplates.VoucherTopUs,
            "check-voucher-top-us.cshtml",
            "US voucher - cheque on top",
            "US business voucher cheque, cheque on top: 3.5in cheque, 3.5in stub, 4in stub.",
            Region: "US", PaperSize: "Letter", ChecksPerPage: 1, Position: CheckPosition.Top,
            SupportedStockTypes: BothStockTypes),

        new BuiltInCheckTemplate(
            CheckTemplates.VoucherMiddleUs,
            "check-voucher-middle-us.cshtml",
            "US voucher - cheque in the middle",
            "US business voucher cheque, cheque in the middle: 3.5in stub, 3.5in cheque, 4in stub.",
            Region: "US", PaperSize: "Letter", ChecksPerPage: 1, Position: CheckPosition.Middle,
            SupportedStockTypes: BothStockTypes),

        new BuiltInCheckTemplate(
            CheckTemplates.VoucherBottomUs,
            "check-voucher-bottom-us.cshtml",
            "US voucher - cheque on the bottom",
            "US business voucher cheque, cheque on the bottom: 4in stub, 3.5in stub, 3.5in cheque.",
            Region: "US", PaperSize: "Letter", ChecksPerPage: 1, Position: CheckPosition.Bottom,
            SupportedStockTypes: BothStockTypes),

        new BuiltInCheckTemplate(
            CheckTemplates.ThreePerPage,
            "check-3up.cshtml",
            "Three cheques per page",
            "Three 8.5 x 3.5in cheques per Letter sheet with a 0.5in feed margin at the foot. No voucher stubs; "
            + "the payment details print on the cheque body.",
            Region: null, PaperSize: "Letter", ChecksPerPage: 3, Position: null,
            SupportedStockTypes: BothStockTypes)
    };

    /// <summary>按模板名查出厂声明（自建模板返回 null）。</summary>
    public static BuiltInCheckTemplate? Find(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : All.FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 解析本次渲染实际生效的模板与每页张数。
    /// </summary>
    /// <remarks>
    /// 渲染器与 <see cref="Services.CheckTemplateCatalog"/> 共用这一处，
    /// 于是「服务写进快照的模板名」与「渲染器真正渲染的那一份」不可能不一致。
    /// <para>
    /// 自建模板不在出厂目录里，其每页张数无从得知，退回按账户版式推断
    /// （<c>ThreePerPage</c> → 3，其余 1）—— 少切一页的代价是模板自己排的版没被打断，
    /// 而猜多了会把一张票拆到两页去。
    /// </para>
    /// </remarks>
    public static CheckTemplateResolution Resolve(string? requestedTemplateName, CheckLayout layout)
    {
        var name = string.IsNullOrWhiteSpace(requestedTemplateName)
            ? CheckTemplates.DefaultForLayout(layout)
            : requestedTemplateName.Trim();

        var builtIn = Find(name);
        return new CheckTemplateResolution(name, builtIn?.ChecksPerPage ?? ChecksPerPageForLayout(layout));
    }

    /// <summary>账户版式推断的每页张数（自建模板的兜底）。</summary>
    private static int ChecksPerPageForLayout(CheckLayout layout)
        => layout == CheckLayout.ThreePerPage ? 3 : 1;
}
