namespace Tnzi.Storage.Services;

/// <summary>
/// 文件存储服务接口（核心文件操作）
/// </summary>
public interface IFileStorageService
{
    // 基础操作
    Task<Result<FileRecord>> SaveAsync(string fileName, Stream stream, bool isTemporary = false);
    Task<Result<Stream>> GetAsync(Guid id);
    /// <summary>
    /// 获取文件流（支持 Range 请求，用于断点续传）
    /// </summary>
    /// <param name="id">文件ID</param>
    /// <param name="rangeStart">Range 起始位置（字节）</param>
    /// <param name="rangeEnd">Range 结束位置（字节）</param>
    /// <returns>文件流和范围信息（Stream, Start, End, TotalLength）</returns>
    Task<Result<(Stream Stream, long Start, long End, long TotalLength)>> GetRangeAsync(
        Guid id,
        long? rangeStart = null,
        long? rangeEnd = null);
    Task<Result<FileRecord>> GetRecordAsync(Guid id);
    /// <summary>
    /// 获取文件信息（返回 FileInfoDto DTO）
    /// </summary>
    Task<Result<FileInfoDto>> GetFileInfoAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<string>> GetUrlAsync(Guid id, int? expiresIn = null);
    /// <summary>
    /// 获取文件缩略图流
    /// </summary>
    Task<Result<Stream>> GetThumbnailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id);
    Task<Result<FileRecord>> GetOrCreateByMd5Async(string md5Hash, string fileName, Stream stream);

    // 批量操作
    Task<Result<IEnumerable<FileRecord>>> SaveManyAsync(IEnumerable<(string fileName, Stream stream)> files);
    Task<Result> DeleteManyAsync(IEnumerable<Guid> ids);
    Task<Result<FileRecord>> RenameAsync(Guid id, string newFileName);
    Task<Result<FileRecord>> CopyAsync(Guid sourceFileId, string? newFileName = null, CancellationToken cancellationToken = default);
    Task<Result<FileStorageStatistics>> GetStatisticsAsync();

    // 便捷方法
    /// <summary>
    /// 保存文件并创建引用
    /// </summary>
    Task<Result<FileRecord>> SaveWithReferenceAsync(string fileName, Stream stream, string entityType, Guid entityId, string fieldName, bool isTemporary = false);
    /// <summary>
    /// 从 byte[] 保存文件
    /// </summary>
    Task<Result<FileRecord>> SaveFromBytesAsync(string fileName, byte[] content, string? contentType = null);
    /// <summary>
    /// 从文件路径保存文件
    /// </summary>
    Task<Result<FileRecord>> SaveFromPathAsync(string filePath, string? contentType = null);

    // 文件查询
    /// <summary>
    /// 查询文件列表（支持分页、筛选、排序）
    /// </summary>
    Task<Result<IPagedList<FileRecord>>> QueryFilesAsync(FileQueryRequest request, CancellationToken cancellationToken = default);

    // 压缩
    Task<Result<FileRecord>> CompressAsync(IEnumerable<Guid> fileIds, string? zipFileName = null, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<FileRecord>>> DecompressAsync(Guid fileId, CancellationToken cancellationToken = default);
}
