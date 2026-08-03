namespace Tnzi.Storage.Services;

/// <summary>
/// 文件分享服务接口
/// </summary>
public interface IFileShareService
{
    /// <summary>
    /// 创建分享链接
    /// </summary>
    Task<Result<FileShare>> CreateShareAsync(Guid fileId, DateTime? expiresAt = null, int? maxAccessCount = null, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取分享信息
    /// </summary>
    Task<Result<FileShare>> GetShareAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销分享
    /// </summary>
    Task<Result> RevokeShareAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分享链接收件人在下载**之前**看到的信息（文件名 / 大小 / 是否要口令）。
    ///
    /// 只在链接确实可用时返回；已撤销 / 已过期 / 次数用尽一律 404，与"令牌根本不存在"
    /// 无法区分 —— 区分开就等于告诉试探者哪些令牌是真的。
    /// **不做口令校验**：收件人需要先知道"这里要口令",口令只把住取字节那一关。
    /// </summary>
    Task<Result<FileSharePreviewDto>> GetSharePreviewAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证分享访问权限。通过时把该文件记进请求作用域的
    /// <see cref="IFileAccessGrantContext"/> —— 分享链接的凭据是**令牌本身**而不是调用者
    /// 的身份，后续取记录 / 取流因此不再要求调用者本人有权。
    /// </summary>
    Task<Result<bool>> ValidateShareAccessAsync(string shareToken, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子占用一次访问配额：在单条 SQL 中检查"启用 + 未超过 MaxAccessCount"并自增 AccessCount，
    /// 消除"读取计数判超限 → 再自增"两步之间的竞态。
    /// Atomically consumes one access slot: a single SQL statement checks "enabled + below MaxAccessCount"
    /// and increments AccessCount, eliminating the TOCTOU race between read-check and increment.
    /// </summary>
    /// <returns>true = 成功占用一次配额；false = 已超限 / 已禁用 / 不存在。</returns>
    Task<Result<bool>> IncrementShareAccessCountAsync(string shareToken, CancellationToken cancellationToken = default);

    // Admin management methods
    /// <summary>
    /// Get all shares for a specific file
    /// </summary>
    Task<Result<IEnumerable<FileShareSummaryDto>>> GetSharesByFileAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Query active shares with paging and filtering
    /// </summary>
    Task<Result<IPagedList<FileShareSummaryDto>>> GetActiveSharesAsync(ActiveSharesQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch revoke multiple shares
    /// </summary>
    Task<Result<int>> BatchRevokeSharesAsync(IEnumerable<Guid> shareIds, CancellationToken cancellationToken = default);
}
