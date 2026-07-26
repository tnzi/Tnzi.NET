namespace Tnzi.Finance.Banking.Services.Interfaces;

/// <summary>
/// 收据采集服务（上传登记 → 提取 → 修正 → 转换为费用/账单草稿）
/// </summary>
public interface IReceiptCaptureService
{
    /// <summary>分页查询</summary>
    Task<Result<IPagedList<ReceiptDto>>> GetPagedAsync(ReceiptQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取收据</summary>
    Task<Result<ReceiptDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>登记收据（上传拿到 fileId 后）</summary>
    Task<Result<ReceiptDto>> CreateAsync(CreateReceiptDto input, CancellationToken cancellationToken = default);

    /// <summary>提取字段（未注册提取器返回 501；失败写 FailReason 可重试）</summary>
    Task<Result<ReceiptDto>> ExtractAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>人工修正提取字段</summary>
    Task<Result<ReceiptDto>> UpdateExtractionAsync(Guid id, UpdateReceiptExtractionDto input, CancellationToken cancellationToken = default);

    /// <summary>转换为费用/账单草稿（止步草稿；双 convert 409）</summary>
    Task<Result<ReceiptConvertResultDto>> ConvertAsync(Guid id, ConvertReceiptDto input, CancellationToken cancellationToken = default);

    /// <summary>删除（Converted 拒绝）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
