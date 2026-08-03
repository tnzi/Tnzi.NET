namespace Tnzi.Storage.Services;

/// <summary>
/// 文件存储服务接口（核心文件操作）
/// </summary>
public interface IFileStorageService
{
    // 基础操作
    /// <summary>
    /// 保存文件。
    /// </summary>
    /// <param name="fileName">原始文件名</param>
    /// <param name="stream">文件内容</param>
    /// <param name="isTemporary">临时文件（ReferenceCount 初始为 0，未被引用时由清理任务回收）</param>
    /// <param name="isPublic">
    /// 标记为公开可读（<see cref="FileRecord.IsPublic"/>）。头像、站点素材这类要以匿名
    /// <c>&lt;img src&gt;</c> 消费的资源传 true；默认 false，文件只有创建者与持
    /// <c>storage.file.view</c> 的管理员可读。
    /// 写入实体上的 <c>[FileField(Public = true)]</c> 字段时框架会自动补上这个标记，
    /// 因此**不必**依赖每个调用方都记得传这个参数。
    /// </param>
    Task<Result<FileRecord>> SaveAsync(string fileName, Stream stream, bool isTemporary = false, bool isPublic = false);
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
    Task<Result<IEnumerable<FileRecord>>> SaveManyAsync(IEnumerable<(string fileName, Stream stream)> files, bool isPublic = false);
    Task<Result> DeleteManyAsync(IEnumerable<Guid> ids);
    Task<Result<FileRecord>> RenameAsync(Guid id, string newFileName);
    Task<Result<FileRecord>> CopyAsync(Guid sourceFileId, string? newFileName = null, CancellationToken cancellationToken = default);
    Task<Result<FileStorageStatistics>> GetStatisticsAsync();

    // 便捷方法
    /// <summary>
    /// 保存文件并创建引用
    /// </summary>
    Task<Result<FileRecord>> SaveWithReferenceAsync(string fileName, Stream stream, string entityType, Guid entityId, string fieldName, bool isTemporary = false, bool isPublic = false);
    /// <summary>
    /// 从 byte[] 保存文件
    /// </summary>
    Task<Result<FileRecord>> SaveFromBytesAsync(string fileName, byte[] content, string? contentType = null, bool isPublic = false);
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

    // Presigned URL
    /// <summary>
    /// Generate a presigned URL for temporary public access to a file (download or upload).
    /// Cloud providers (S3, R2, Azure) support this natively; LocalStorage returns a controller-based URL.
    /// </summary>
    /// <param name="id">File ID</param>
    /// <param name="expiresInSeconds">URL expiration in seconds (default 3600 = 1 hour)</param>
    /// <param name="httpMethod">HTTP method: GET for download, PUT for direct upload (default GET)</param>
    /// <returns>Presigned URL string</returns>
    Task<Result<string>> GetPresignedUrlAsync(Guid id, int expiresInSeconds = 3600, string httpMethod = "GET");

    // User/Tenant storage usage
    /// <summary>
    /// Get storage usage statistics for a specific user (total files and total size).
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>User storage usage statistics</returns>
    Task<Result<UserStorageUsage>> GetUserStorageUsageAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get storage usage ranked by top users (ordered by total size descending).
    /// </summary>
    /// <param name="top">Number of top users to return (default 20)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of user storage usages</returns>
    Task<Result<IEnumerable<UserStorageUsage>>> GetTopUsersByStorageAsync(int top = 20, CancellationToken cancellationToken = default);

    // File integrity verification
    /// <summary>
    /// Verify integrity of a single file: checks physical existence and MD5 hash match.
    /// </summary>
    Task<Result<FileIntegrityResult>> VerifyFileIntegrityAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch verify integrity of all files (or a subset). Returns only problematic files in details.
    /// </summary>
    /// <param name="maxFiles">Maximum number of files to check (default 100, 0 = all)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Result<BatchIntegrityResult>> BatchVerifyIntegrityAsync(int maxFiles = 100, CancellationToken cancellationToken = default);

    // File tags
    /// <summary>
    /// Set tags for a file (replaces existing tags). Returns the updated
    /// FileRecord entity; controllers project it to the safe FileRecordDto.
    /// </summary>
    Task<Result<FileRecord>> SetFileTagsAsync(Guid fileId, List<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get files by tag (supports paging).
    /// </summary>
    Task<Result<IPagedList<FileRecord>>> GetFilesByTagAsync(string tag, int pageIndex = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    // File metadata
    /// <summary>
    /// Set metadata for a file (replaces existing metadata). Returns the updated
    /// FileRecord entity; controllers project it to the safe FileRecordDto.
    /// </summary>
    Task<Result<FileRecord>> SetMetadataAsync(Guid fileId, Dictionary<string, string> metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get metadata for a file.
    /// </summary>
    Task<Result<Dictionary<string, string>>> GetMetadataAsync(Guid fileId, CancellationToken cancellationToken = default);

    // File visibility

    /// <summary>
    /// 改一个已存在文件的可见性（<see cref="FileRecord.IsPublic"/>）。
    /// 公开只影响读取，不影响变更 —— 公开的文件仍然只有创建者 / 持 <c>storage.file.update</c>
    /// 的管理员能改能删。
    ///
    /// 需要**变更**权限（不是读取权限）：把私密文件改成人人可读是一次授权决策，
    /// 只有能改这个文件的人才有资格做；无权者按既有约定返回 404 而非 403。
    /// </summary>
    Task<Result<FileRecord>> SetFileVisibilityAsync(Guid fileId, bool isPublic, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 <c>[FileField(Public = true)]</c> 的字段声明回填历史数据：扫描文件引用表，
    /// 把所有被声明为公开的字段引用到的文件标记为公开可读，返回本次改动的文件数。
    ///
    /// 用途是**升级到声明式公开之后的一次性回填** —— 声明只对之后写入的引用生效，
    /// 早已存在的头像不会自己重存一遍。幂等：已公开的记录不会被重复写。
    ///
    /// 只升不降：本方法从不把文件改回私密，故对私密文件库无风险。
    /// </summary>
    Task<Result<int>> SyncPublicFlagsFromReferencesAsync(CancellationToken cancellationToken = default);

    // Signed access (browser-renderable private files)

    /// <summary>
    /// 为一个私密文件签发短时访问令牌，让浏览器发起的请求（<c>&lt;img src&gt;</c> /
    /// <c>&lt;a download&gt;</c> / <c>&lt;video&gt;</c>）也能取到它 —— 那些请求带不了
    /// Authorization 头，而框架的认证是纯 Bearer。
    ///
    /// 签发时走完整读权限判定：调用者读不了这个文件就拿不到令牌（同样以 404 掩盖存在性）。
    /// 令牌只对这一个文件、这一小段时间有效，与「公开」是两回事。
    /// </summary>
    /// <param name="fileId">文件 ID</param>
    /// <param name="expiresInSeconds">有效期（秒）；不传用 <c>Storage:SignedUrlTtlSeconds</c></param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result<FileAccessTokenDto>> CreateAccessTokenAsync(Guid fileId, int? expiresInSeconds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量签发访问令牌。**读不了的 id 直接从结果中省略**，不让整批失败：
    /// 一页图片里混进一个越权 id 时，其余图片仍应正常显示，而省略本身也不透露
    /// 那个 id 上是否真有文件。
    /// </summary>
    Task<Result<IReadOnlyList<FileAccessTokenDto>>> CreateAccessTokensAsync(IReadOnlyCollection<Guid> fileIds, int? expiresInSeconds = null, CancellationToken cancellationToken = default);
}
