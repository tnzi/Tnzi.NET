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
    /// <summary>本次生效的模板名（已解析：含"档案没配就按版式取出厂默认"的回退结果）</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>本次生效的版式名（<c>Voucher</c> / <c>ThreePerPage</c>）</summary>
    /// <remarks>
    /// 以字符串而非枚举暴露：<c>@Model</c> 是 dynamic，模板里写 <c>== "ThreePerPage"</c>
    /// 比让模板作者去 using 一个后端枚举现实得多。判定「一页几张」请用
    /// <see cref="ChecksPerPage"/>，判定票纸请用 <see cref="IsPrePrinted"/>。
    /// </remarks>
    public string Layout { get; set; } = string.Empty;

    /// <summary>本次生效的票纸类型名（<c>PrePrinted</c> / <c>Blank</c>）</summary>
    public string StockType { get; set; } = string.Empty;

    /// <summary>每页支票张数（由生效模板声明；<see cref="Pages"/> 已按此切好）</summary>
    public int ChecksPerPage { get; set; } = 1;

    /// <summary>
    /// 预览模式：支票号是"下一个待分配号"的预览值，尚未开票。模板据此打不可流通水印。
    /// </summary>
    public bool IsPreview { get; set; }

    /// <summary>预览水印文案（英文，面向用户）</summary>
    public string PreviewLabel { get; set; } = string.Empty;

    /// <summary>
    /// 样张模式：占位数据渲染的版式样票（供选版式时与手上的票纸比对）
    /// </summary>
    /// <remarks>
    /// 模板据此把<b>票纸自带的元素</b>在屏幕上区分出来（出厂模板的做法是给页根加
    /// <c>specimen</c> 类、样式表把 <see cref="PrePrintedClass"/> 的元素淡显）。
    /// <para>
    /// ★ <b>只有样张该这么做</b>：付款预览（<c>IsPreview</c> 而非本标志）屏幕上就该显示
    /// 完整票面——那时的任务是校对整张支票。真票与预览的渲染产物不受本标志影响。
    /// </para>
    /// </remarks>
    public bool IsSpecimen { get; set; }

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

    /// <summary>本次渲染的全部支票（一张一张，不分页）</summary>
    /// <remarks>
    /// 每页一张的版式直接 <c>@foreach (var chk in Model.Checks)</c> 即可（既有模板都这么写）；
    /// 每页多张的版式请用 <see cref="Pages"/>，切页已经替模板做好了。
    /// </remarks>
    public List<CheckDocumentItem> Checks { get; set; } = new();

    /// <summary>
    /// 按 <see cref="ChecksPerPage"/> 切好的页（<see cref="Checks"/> 的分组视图，同一批数据）
    /// </summary>
    /// <remarks>
    /// 切页放在工厂里而不是让模板自己按下标步进：模板是用户可编辑内容，
    /// 一个写歪的下标循环会安静地漏印一张支票 —— 而支票号已经分配出去了。
    /// </remarks>
    public List<CheckDocumentPage> Pages { get; set; } = new();
}

/// <summary>
/// 一页（每页多张的版式用；每页一张时每页恰好一项）
/// </summary>
public class CheckDocumentPage
{
    /// <summary>页码（从 1 起，模板可打"第 N 页"）</summary>
    public int Number { get; set; }

    /// <summary>本页的支票（最后一页可能不足 <see cref="CheckDocumentModel.ChecksPerPage"/> 张）</summary>
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

    /// <summary>金额大写（<c>*</c> 填满行尾防改写）；币种字样由模板单独排版的元素提供</summary>
    public string AmountInWordsText { get; set; } = string.Empty;

    /// <summary>
    /// 金额大写 + 币种词，再以 <c>*</c> 填满行尾（<c>... and 56/100 Dollars ******</c>）
    /// </summary>
    /// <remarks>
    /// 给<b>大写金额与数字金额同处一行</b>的版式用（CPA-006 §5.4.1 Figure C 开窗信封版）：
    /// 那一行的右端就是数字金额框，放不下单独的 "DOLLARS" 元素，规范第 9 条因此许可
    /// 把币种词并进机打的大写金额。用这个字段的模板<b>不要</b>再排
    /// <see cref="CurrencyLabel"/>，否则票面上币种字样出现两次。
    /// </remarks>
    public string AmountInWordsWithCurrencyText { get; set; } = string.Empty;

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

    /// <summary>
    /// 存根附加行（消费应用自己域里的「标签 : 值」；<b>空列表 = 存根按出厂样子排</b>）
    /// </summary>
    /// <remarks>
    /// 模板在既有固定行<b>之后</b>追加它们，因此没有附加行时渲染结果与从前逐字相同。
    /// 条数与长度在银行域已归一化（<c>CheckStubLineLimits</c>），模板不必也不该再限制。
    /// </remarks>
    public List<CheckDocumentStubLine> StubLines { get; set; } = new();
}

/// <summary>
/// 存根上的一行附加信息（已格式化，模板直出）
/// </summary>
public class CheckDocumentStubLine
{
    /// <summary>字段名（存根表格左列）</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>字段值（右列；可空 —— 只有标签的一行是分节标题）</summary>
    public string? Value { get; set; }
}
