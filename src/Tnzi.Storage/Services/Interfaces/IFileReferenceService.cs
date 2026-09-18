namespace Tnzi.Storage.Services;

/// <summary>
/// 文件引用管理服务接口
/// </summary>
public interface IFileReferenceService
{
    // 引用确认与更新
    Task<Result> ConfirmReferenceAsync(Guid fileId, string entityType, Guid entityId, string fieldName);
    Task<Result> UpdateReferenceAsync(Guid? oldFileId, Guid? newFileId, string entityType, Guid entityId, string fieldName);
    Task<Result> BatchConfirmReferencesAsync(IEnumerable<FileReferenceInfo> references, CancellationToken cancellationToken = default);
    Task<Result> BatchUpdateReferencesAsync(string entityType, Guid entityId, Dictionary<string, IEnumerable<Guid>> fieldFileIds, CancellationToken cancellationToken = default);

    // 引用查询
    Task<Result<IEnumerable<FileReferenceDto>>> GetReferencesAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<FileReferenceDto>>> GetReferencesByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);
    Task<Result<FileReferenceStatistics>> GetReferenceStatisticsAsync(string? entityType = null, CancellationToken cancellationToken = default);

    // 引用计数同步 —— 只对临时记录（IsTemporary = true）有完整的信息来源：
    // 正式上传与 MD5 复用带**隐式**持有者（不产生引用行），按引用行重算会把它们归零、交给孤儿回收。

    /// <summary>
    /// 按非临时引用行重算一条<b>临时</b>记录的引用计数；正式记录答 400。
    /// </summary>
    Task<Result<int>> SyncReferenceCountAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按非临时引用行重算<b>全部临时记录</b>的引用计数，返回被改动的条数；正式记录一律不动。
    /// </summary>
    Task<Result<int>> SyncAllReferenceCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验引用计数：临时记录要求与引用行数相等；正式记录只要求不低于引用行数（隐式持有者数未知）。
    /// </summary>
    Task<Result<bool>> ValidateReferenceCountAsync(Guid fileId, CancellationToken cancellationToken = default);

    // 临时文件管理
    Task<Result<int>> CleanupTemporaryFilesAsync(TimeSpan? olderThan = null);
    Task<Result<IEnumerable<FileRecord>>> GetTemporaryFilesAsync(TimeSpan? olderThan = null);
}
