namespace Tnzi.Storage.Services;

/// <summary>
/// 文件清理服务接口
/// </summary>
public interface IFileCleanupService
{
    /// <summary>
    /// 执行完整的清理任务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理结果</returns>
    Task<CleanupResult> CleanupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理临时文件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的文件数量</returns>
    Task<int> CleanupTemporaryFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理僵尸文件（ReferenceCount=0）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的文件数量</returns>
    Task<int> CleanupOrphanFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理孤立引用（引用的实体已不存在）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的引用数量</returns>
    Task<int> CleanupOrphanReferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理过期的分片上传会话及其残留分片（含物理文件）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的会话数量</returns>
    Task<int> CleanupExpiredUploadSessionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 清理结果
/// </summary>
public class CleanupResult
{
    /// <summary>
    /// 清理的临时文件数量
    /// </summary>
    public int TemporaryFilesDeleted { get; set; }

    /// <summary>
    /// 清理的僵尸文件数量
    /// </summary>
    public int OrphanFilesDeleted { get; set; }

    /// <summary>
    /// 清理的孤立引用数量
    /// </summary>
    public int OrphanReferencesDeleted { get; set; }

    /// <summary>
    /// 清理的过期分片上传会话数量
    /// </summary>
    public int ExpiredSessionsDeleted { get; set; }

    /// <summary>
    /// 清理是否成功
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// 错误消息（如果有）
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// 清理总数
    /// </summary>
    public int TotalDeleted => TemporaryFilesDeleted + OrphanFilesDeleted + OrphanReferencesDeleted + ExpiredSessionsDeleted;
}
