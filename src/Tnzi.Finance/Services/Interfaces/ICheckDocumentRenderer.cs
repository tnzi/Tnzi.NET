namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 支票 PDF 渲染器（契约留 Finance 核心，核心零 PdfSharp 引用）
/// </summary>
/// <remarks>
/// 默认实现（基于 PdfSharp 的 <c>PdfSharpCheckRenderer</c>）由可选子模块 <c>Tnzi.Finance.Documents</c> 提供，
/// 把 PdfSharp 依赖隔离在该子模块，纯会计消费者不被传递拉入 PdfSharp。消费应用也可注册自己的实现整体覆盖。
/// 未加载该子模块时 <c>ICheckService</c> 的 print / reprint / calibration 返回 501 引导（同 <c>IReceiptExtractor</c> 兜底）。
/// </remarks>
public interface ICheckDocumentRenderer
{
    /// <summary>渲染一份或多份支票为单个 PDF。</summary>
    Result<byte[]> Render(CheckRenderRequest request);

    /// <summary>渲染一页校准标尺测试页（用于调节 OffsetXMm/OffsetYMm）。</summary>
    Result<byte[]> RenderCalibration(CheckRenderRequest request);
}

/// <summary>
/// 支票渲染请求（账户级版式 + 偏移 + MICR 参数 + 逐张支票数据）
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

    public List<CheckRenderItem> Checks { get; set; } = new();
}

/// <summary>
/// 单张支票的渲染数据
/// </summary>
public class CheckRenderItem
{
    public long CheckNumber { get; set; }
    public string? PayeeName { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string AmountInWords { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public string? Memo { get; set; }
}
