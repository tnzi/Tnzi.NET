namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 收据字段提取器（核心契约，零 Storage/AI 引用）
/// </summary>
/// <remarks>
/// 实现是消费层策略（选模型 / 写 prompt），不属框架能力：核心只定义契约 + 501 引导，
/// 由消费应用注册实现（vision + 结构化输出 / PDF 文本抽取，参见 Finance 模块文档 recipe）。
/// 按 <see cref="ReceiptExtractionRequest.FileId"/> 传递（核心不引用 Storage：实现方自行取流）。
/// 未注册任何实现时 <c>IReceiptCaptureService.ExtractAsync</c> 返回 501 引导。
/// </remarks>
public interface IReceiptExtractor
{
    /// <summary>从存储中的文件提取收据字段。</summary>
    Task<Result<ReceiptExtractionResult>> ExtractAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 收据提取请求
/// </summary>
public class ReceiptExtractionRequest
{
    /// <summary>存储文件ID</summary>
    public Guid FileId { get; set; }

    /// <summary>文件名（可选，仅供实现方参考）</summary>
    public string? FileName { get; set; }

    /// <summary>内容类型（可选；实现方通常从存储元数据解析）</summary>
    public string? ContentType { get; set; }

    /// <summary>币种提示（可选）</summary>
    public string? HintCurrency { get; set; }
}

/// <summary>
/// 收据提取结果
/// </summary>
public class ReceiptExtractionResult
{
    public string? VendorName { get; set; }
    public DateTime? DocDate { get; set; }
    public string? Currency { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? Total { get; set; }
    public string? Reference { get; set; }
    public List<ReceiptExtractionLineItem> LineItems { get; set; } = new();

    /// <summary>提取置信度（0-1）</summary>
    public decimal Confidence { get; set; }

    /// <summary>原始文本（PDF 抽取或 OCR，可选）</summary>
    public string? RawText { get; set; }
}

/// <summary>
/// 收据行项（提取结果）
/// </summary>
public class ReceiptExtractionLineItem
{
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Amount { get; set; }
}
