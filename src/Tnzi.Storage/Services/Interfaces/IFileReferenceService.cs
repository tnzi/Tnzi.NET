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

    // 引用计数同步
    Task<Result<int>> SyncReferenceCountAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<Result<int>> SyncAllReferenceCountsAsync(CancellationToken cancellationToken = default);
    Task<Result<bool>> ValidateReferenceCountAsync(Guid fileId, CancellationToken cancellationToken = default);

    // 临时文件管理
    Task<Result<int>> CleanupTemporaryFilesAsync(TimeSpan? olderThan = null);
    Task<Result<IEnumerable<FileRecord>>> GetTemporaryFilesAsync(TimeSpan? olderThan = null);
}
