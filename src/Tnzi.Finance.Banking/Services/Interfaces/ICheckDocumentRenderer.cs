namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 支票文档渲染器（契约在银行域，本模块零渲染库引用）
/// </summary>
/// <remarks>
/// 默认实现由可选子模块 <c>Tnzi.Finance.Documents</c> 提供：模板驱动的 <c>TemplateCheckRenderer</c>
/// （渲染入库的 Razor 支票模板 → HTML，默认注册）与基于 PdfSharp 的 <c>PdfSharpCheckRenderer</c>
/// （PDF 备选，消费应用可显式选用）。把渲染库依赖隔离在该子模块，纯会计消费者不被传递拉入。
/// 消费应用也可注册自己的实现整体覆盖。
/// 未加载该子模块时 <c>ICheckService</c> 的 print / preview / reprint / calibration 返回 501 引导
/// （同 <c>IReceiptExtractor</c> 兜底）。
/// </remarks>
public interface ICheckDocumentRenderer
{
    /// <summary>渲染一份或多份支票为单个文档。</summary>
    Result<byte[]> Render(CheckRenderRequest request);

    /// <summary>渲染一页校准标尺测试页（用于调节 OffsetXMm/OffsetYMm）。</summary>
    Result<byte[]> RenderCalibration(CheckRenderRequest request);

    /// <summary>
    /// 输出内容类型（如 <c>application/pdf</c> / <c>text/html</c>），供调用方设置下载响应的 MIME。
    /// </summary>
    /// <remarks>默认 PDF，保持既有渲染器（PdfSharp 及消费应用自有实现）零改动。</remarks>
    string ContentType => "application/pdf";

    /// <summary>输出文件扩展名（含点，如 <c>.pdf</c> / <c>.html</c>）。</summary>
    /// <remarks>默认 <c>.pdf</c>，理由同 <see cref="ContentType"/>。</remarks>
    string FileExtension => ".pdf";

    /// <summary>
    /// 异步渲染（模板驱动实现的天然形态：模板加载与 Razor 编译都是异步的）。
    /// </summary>
    /// <remarks>
    /// 默认实现委托同步 <see cref="Render"/>，既有实现无需改动即可被异步调用方消费
    /// （接口以默认方法演进，见框架 CLAUDE.md「Interface evolution via default methods」）。
    /// </remarks>
    Task<Result<byte[]>> RenderAsync(CheckRenderRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Render(request));

    /// <summary>异步渲染校准标尺测试页。默认实现委托同步 <see cref="RenderCalibration"/>。</summary>
    Task<Result<byte[]>> RenderCalibrationAsync(CheckRenderRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(RenderCalibration(request));
}

/// <summary>
/// 支票渲染请求（账户级版式 + 偏移 + MICR 参数 + 出票方身份 + 逐张支票数据）
/// </summary>
public class CheckRenderRequest
{
    public CheckLayout Layout { get; set; } = CheckLayout.Voucher;
    public CheckStockType StockType { get; set; } = CheckStockType.PrePrinted;

    /// <summary>全票面水平/垂直偏移（毫米，用于对齐预印票纸）</summary>
    public decimal OffsetXMm { get; set; }
    public decimal OffsetYMm { get; set; }

    public BankNumberScheme Scheme { get; set; } = BankNumberScheme.UsAba;
    public string? BankName { get; set; }
    public string? AccountName { get; set; }
    public string? RoutingNumber { get; set; }
    public string? InstitutionNumber { get; set; }
    public string? TransitNumber { get; set; }

    /// <summary>解密后的账号明文（白纸 MICR 拼装用；预印票纸或无账号为 null）</summary>
    public string? AccountNumberPlain { get; set; }

    /// <summary>E-13B MICR 字体文件路径（白纸打印必需）</summary>
    public string? MicrFontPath { get; set; }

    /// <summary>
    /// 支票号补零到的位数（null = <see cref="CheckNumberFormat.DefaultDigits"/>）。
    /// </summary>
    /// <remarks>
    /// ★ <b>票面与 MICR 串行号必须同宽</b>，所以这是<b>一个</b>字段而不是两个 ——
    /// 两个字段配不一致的话，纸上写着一个号、磁码行读出另一个，而两处看起来都很正常。
    /// 规则在 <see cref="CheckNumberFormat"/>，消费应用可以用同一个方法把自己界面上的支票号
    /// 渲染成与纸上逐字相同的样子。
    /// </remarks>
    public int? CheckNumberDigits { get; set; }

    /// <summary>
    /// 支票版式模板名（模板驱动渲染器用；null = 渲染器的默认模板）。
    /// 取自 <c>BankAccount.CheckTemplateName</c>，不同银行可指向不同模板。
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>
    /// 出票方（本公司）身份：抬头、地址、联系方式、logo 与签名。
    /// null = 未配置（渲染器应优雅降级为不打抬头/签名，而非报错）。
    /// </summary>
    public CheckIssuerInfo? Issuer { get; set; }

    /// <summary>
    /// 预览模式：支票号是"下一个待分配号"的预览值，尚未分配、未登记、未过账。
    /// 渲染器据此打上不可流通的水印/标记，避免预览件被误当成真票使用。
    /// </summary>
    public bool IsPreview { get; set; }

    /// <summary>
    /// 不可流通标记的文案（null = 渲染器的默认 <c>PREVIEW - NOT NEGOTIABLE</c>）
    /// </summary>
    /// <remarks>
    /// 仅在 <see cref="IsPreview"/> 为真时有意义。存在的理由是<b>样张</b>：
    /// 它拿占位数据渲染、会被下载被打印被截图，标成 "PREVIEW" 会让人以为那是某笔真实付款的预览。
    /// </remarks>
    public string? PreviewLabel { get; set; }

    /// <summary>
    /// 样张模式：占位数据渲染的版式样票，供选版式时与手上的票纸比对
    /// </summary>
    /// <remarks>
    /// 蕴含 <see cref="IsPreview"/>（样张也是不可流通的），但比它多一件事：
    /// 样张要在<b>屏幕上</b>把「票纸自带的元素」与「打印机这次真打上去的元素」分开。
    /// <para>
    /// ★ 为什么单独一个标志、而不是复用 <see cref="IsPreview"/>：付款预览屏幕上
    /// <b>就该显示完整票面</b>——那时的任务是校对整张支票，把预印元素淡下去会妨碍校对。
    /// 需要第三种呈现的只有样张。
    /// </para>
    /// </remarks>
    public bool IsSpecimen { get; set; }

    public List<CheckRenderItem> Checks { get; set; } = new();
}

/// <summary>
/// 出票方（本公司）身份，印在支票抬头与签名区
/// </summary>
/// <remarks>
/// 由 <c>CheckService</c> 从 System General（<c>System:CompanyName</c> / <c>Address</c> / <c>Phone</c> …）
/// 与 <c>FinanceOptions</c> 的支票签名配置解析填充，渲染器只消费不解析。
/// </remarks>
public class CheckIssuerInfo
{
    /// <summary>公司/出票人名称</summary>
    public string? Name { get; set; }

    /// <summary>地址（已按行拆分）</summary>
    public List<string> AddressLines { get; set; } = new();

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? WebsiteUrl { get; set; }

    /// <summary>公司 logo 图片地址（URL 或 data URI）</summary>
    public string? LogoUrl { get; set; }

    /// <summary>签名图片地址（URL 或 data URI）</summary>
    public string? SignatureImageUrl { get; set; }

    /// <summary>签名人姓名（印在签名线下方）</summary>
    public string? SignatureName { get; set; }

    /// <summary>签名人职务</summary>
    public string? SignatureTitle { get; set; }

    /// <summary>是否有可打印的身份信息（全空时渲染器跳过抬头区）</summary>
    public bool HasIdentity =>
        !string.IsNullOrWhiteSpace(Name) || AddressLines.Count > 0 || !string.IsNullOrWhiteSpace(LogoUrl);
}

/// <summary>
/// 存根联上的一行「标签 : 值」（消费应用自己域里的凭据信息）
/// </summary>
/// <remarks>
/// 存根回答的是「这张票为什么开」，而那个理由长在<b>消费应用的域里</b>——案卷号、工单号、
/// 保单号、租约期次。框架不认识这些字段，也不该认识；它只提供一个放它们的位置。
/// <para>
/// ★ 之所以是「标签 + 值」而不是一段自由文本：存根是拿来<b>对账</b>的，
/// 收款方与自己的档案都要按字段核对。挤进 <see cref="CheckRenderItem.Memo"/> 的一行字
/// 谁也对不了 —— 那正是本原语存在的理由。
/// </para>
/// <para>
/// 条数与长度上限见 <c>CheckStubLineLimits</c>：存根是有限的物理空间，
/// 一个失控的列表会把票面顶变形。
/// </para>
/// </remarks>
/// <param name="Label">字段名（如 <c>File No.</c>）。空白的行会被丢弃。</param>
/// <param name="Value">字段值。可空——只有标签的一行仍然有意义（作分节标题）。</param>
public sealed record CheckStubLine(string Label, string? Value);

/// <summary>
/// 单张支票的渲染数据
/// </summary>
public class CheckRenderItem
{
    public long CheckNumber { get; set; }
    public string? PayeeName { get; set; }

    /// <summary>收款人地址（已按行拆分，可空；用于开窗信封版式）</summary>
    public List<string> PayeeAddressLines { get; set; } = new();

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string AmountInWords { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public string? Memo { get; set; }

    /// <summary>关联付款单编号（存根联明细用）</summary>
    public string? PaymentNumber { get; set; }

    /// <summary>关联付款单参考号（存根联明细用）</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// 消费应用附加到本张支票存根上的「标签 : 值」行（空 = 存根按出厂样子排，与从前逐字相同）
    /// </summary>
    /// <remarks>
    /// 由 <c>ICheckStubLineProvider</c>（有付款单的路径）或请求 DTO（临时预览）填充，
    /// 已经过 <c>CheckStubLineLimits</c> 归一化。模板在既有固定行<b>之后</b>追加它们。
    /// </remarks>
    public List<CheckStubLine> StubLines { get; set; } = new();
}
