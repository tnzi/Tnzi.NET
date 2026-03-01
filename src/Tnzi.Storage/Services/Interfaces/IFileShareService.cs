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
    /// 验证分享访问权限
    /// </summary>
    Task<Result<bool>> ValidateShareAccessAsync(string shareToken, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 增加分享访问计数
    /// </summary>
    Task<Result> IncrementShareAccessCountAsync(string shareToken, CancellationToken cancellationToken = default);

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
