namespace Tnzi.Finance.Documents.Models;

/// <summary>
/// 支票模板的绑定模型（<c>@Model</c> 根）
/// </summary>
/// <remarks>
/// 所有与呈现相关的取值/格式化都在 <c>CheckDocumentModelFactory</c> 里完成，模板只负责**版式**：
/// 模板里不做金额/日期格式化、不做币种判断、不做 MICR 拼装，改版式无需改代码、改代码无需改版式。
/// 模板经 <c>ITemplateRenderService</c> 渲染（RazorEngineCore，<c>@Model</c> 为 dynamic），
/// 因此本类及其成员必须是 public。
/// </remarks>
public class CheckDocumentModel
{
    /// <summary>
    /// 预览模式：支票号是"下一个待分配号"的预览值，尚未开票。模板据此打不可流通水印。
    /// </summary>
    public bool IsPreview { get; set; }

    /// <summary>预览水印文案（英文，面向用户）</summary>
    public string PreviewLabel { get; set; } = string.Empty;

    /// <summary>预印票纸模式（票纸上已印公司/银行/支票号/$/PAY TO/MICR）</summary>
    public bool IsPrePrinted { get; set; }

    /// <summary>
    /// 预印元素的 CSS class：预印票纸下为 <c>noprint</c>（屏幕预览可见、打印时
    /// <c>visibility:hidden</c> 保留占位防坐标漂移），白纸模式下为空串（照常打印）。
    /// </summary>
    public string PrePrintedClass { get; set; } = string.Empty;

    /// <summary>是否打印 MICR 磁码行（仅白纸票纸且账号可解密时为 true）</summary>
    public bool ShowMicr { get; set; }

    /// <summary>
    /// 全票面平移校准的内联样式（<c>transform: translate(XMm, YMm)</c>；零偏移时为空串）
    /// </summary>
    public string OffsetStyle { get; set; } = string.Empty;

    /// <summary>出票方（本公司）抬头与签名</summary>
    public CheckIssuerInfo Issuer { get; set; } = new();

    /// <summary>付款银行标识</summary>
    public CheckBankView Bank { get; set; } = new();

    /// <summary>本次渲染的全部支票（每张一页，页间 <c>page-break-after</c>）</summary>
    public List<CheckDocumentItem> Checks { get; set; } = new();
}

/// <summary>
/// 付款银行标识（票面左下角银行区）
/// </summary>
public class CheckBankView
{
    /// <summary>银行名称</summary>
    public string? Name { get; set; }

    /// <summary>银行账户档案名称</summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// 人可读的路由标识（CA：<c>Transit 12345 - Institution 003</c>；US：<c>Routing 123456789</c>）。
    /// 空白票纸的机器可读磁码在 <see cref="CheckDocumentItem.MicrGlyphs"/>。
    /// </summary>
    public string? RoutingLine { get; set; }
}

/// <summary>
/// 单张支票的票面数据（已格式化，模板直出）
/// </summary>
public class CheckDocumentItem
{
    /// <summary>支票号（预印票纸上已印，白纸模式现打）</summary>
    public string CheckNumberText { get; set; } = string.Empty;

    public string? PayeeName { get; set; }

    /// <summary>收款人地址（按行，可空）</summary>
    public List<string> PayeeAddressLines { get; set; } = new();

    /// <summary>金额数字（防篡改前缀 <c>***</c>，如 <c>***1,234.56</c>）</summary>
    public string AmountText { get; set; } = string.Empty;

    /// <summary>金额大写（<c>*</c> 填满行尾防改写）</summary>
    public string AmountInWordsText { get; set; } = string.Empty;

    /// <summary>币种代码</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>法定金额行尾的币种字样（USD/CAD → <c>DOLLARS</c>，其余为币种代码）</summary>
    public string CurrencyLabel { get; set; } = string.Empty;

    /// <summary>签发日期（<c>yyyy MM dd</c>，对齐 CPA-006 的 Y Y Y Y M M D D 格框）</summary>
    public string IssueDateText { get; set; } = string.Empty;

    /// <summary>签发日期（ISO，存根联用）</summary>
    public string IssueDateIso { get; set; } = string.Empty;

    public string? Memo { get; set; }

    /// <summary>关联付款单编号（存根联明细）</summary>
    public string? PaymentNumber { get; set; }

    /// <summary>关联付款单参考号（存根联明细）</summary>
    public string? Reference { get; set; }

    /// <summary>MICR 行（Unicode OCR 符号 ⑆⑈⑉，屏幕可读；白纸模式才有值）</summary>
    public string? MicrLine { get; set; }

    /// <summary>MICR 行（映射到 E-13B 字体码位 A/B/C/D，打印用；白纸模式才有值）</summary>
    public string? MicrGlyphs { get; set; }
}
