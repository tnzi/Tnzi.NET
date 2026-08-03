namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// EFT 批次服务（组建付款批次并生成 NACHA / CPA-005 文件）
/// </summary>
/// <remarks>
/// 队列 = Posted Outbound + PaymentMethod==BankTransfer + 往来方有默认 remit-to 账户 + 未在非 Voided 批。
/// 生成时明文账号仅在内存栈拼装、文件立即加密固化（不落 Storage）；Generated 后不可改（要改须作废重建）。
/// </remarks>
public interface IEftService
{
    /// <summary>可入批队列</summary>
    Task<Result<List<EftQueueItemDto>>> GetQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>分页查询批次</summary>
    Task<Result<IPagedList<EftBatchDto>>> GetPagedAsync(EftBatchQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取批次（含行）</summary>
    Task<Result<EftBatchDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建草稿批次（校验币种/scheme 与格式匹配）</summary>
    Task<Result<EftBatchDto>> CreateBatchAsync(CreateEftBatchDto input, CancellationToken cancellationToken = default);

    /// <summary>生成文件（Draft → 组文件 + 加密固化 + 分配号 → Generated）</summary>
    Task<Result<EftBatchDto>> GenerateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>下载文件（解密返回）</summary>
    Task<Result<EftFileDto>> DownloadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>作废批次（硬删行释放付款可重入）</summary>
    Task<Result<EftBatchDto>> VoidBatchAsync(Guid id, VoidEftBatchDto input, CancellationToken cancellationToken = default);
}
