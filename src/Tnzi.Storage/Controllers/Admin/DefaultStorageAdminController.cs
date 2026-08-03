namespace Tnzi.Storage.Controllers.Admin;

/// <summary>
/// 文件存储管理控制器基类
/// 提供文件管理类操作 API 端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/files")]
[ApiAuthorize(PermissionName = "storage.file.view")]
public class DefaultStorageAdminController : ApiAdminControllerBase
{
    protected readonly IFileStorageService FileStorageService;
    protected readonly IFileReferenceService FileReferenceService;
    protected readonly IFileShareService FileShareService;

    /// <summary>
    /// 初始化文件存储管理控制器基类
    /// </summary>
    public DefaultStorageAdminController(
        IFileStorageService fileStorageService,
        IFileReferenceService fileReferenceService,
        IFileShareService fileShareService)
    {
        FileStorageService = Check.NotNull(fileStorageService);
        FileReferenceService = Check.NotNull(fileReferenceService);
        FileShareService = Check.NotNull(fileShareService);
    }

    /// <summary>
    /// 批量删除文件
    /// </summary>
    [HttpDelete("batch")]
    [ApiAuthorize(PermissionName = "storage.file.delete")]
    public virtual async Task<ApiResult> DeleteMany([FromBody] IEnumerable<Guid> ids)
    {
        var result = await FileStorageService.DeleteManyAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取文件存储统计信息
    /// </summary>
    [HttpGet("statistics")]
    public virtual async Task<ApiResult<FileStorageStatistics>> GetStatistics()
    {
        var result = await FileStorageService.GetStatisticsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 清理临时文件
    /// </summary>
    [HttpPost("cleanup-temporary")]
    [ApiAuthorize(PermissionName = "storage.file.delete")]
    public virtual async Task<ApiResult<int>> CleanupTemporaryFiles([FromQuery] int? olderThanHours = null)
    {
        var olderThan = olderThanHours.HasValue
            ? TimeSpan.FromHours(olderThanHours.Value)
            : TimeSpan.FromHours(24);

        var result = await FileReferenceService.CleanupTemporaryFilesAsync(olderThan);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取临时文件列表
    /// </summary>
    [HttpGet("temporary")]
    public virtual async Task<ApiResult<IEnumerable<FileRecordDto>>> GetTemporaryFiles([FromQuery] int? olderThanHours = null)
    {
        var olderThan = olderThanHours.HasValue
            ? TimeSpan.FromHours(olderThanHours.Value)
            : TimeSpan.FromHours(24);

        var result = await FileReferenceService.GetTemporaryFilesAsync(olderThan);
        return result.Map(items => items.Select(r => r.MapTo<FileRecordDto>())).ToApiResult();
    }

    /// <summary>
    /// 查询文件列表（支持分页、筛选、排序）
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<FileRecordDto>>> QueryFiles([FromBody] FileQueryRequest request)
    {
        var result = await FileStorageService.QueryFilesAsync(request);
        return result.Map(MapPaged).ToApiResult();
    }

    /// <summary>
    /// 获取文件的所有引用
    /// </summary>
    [HttpGet("{id:guid}/references")]
    public virtual async Task<ApiResult<IEnumerable<FileReferenceDto>>> GetReferences(Guid id)
    {
        var result = await FileReferenceService.GetReferencesAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取实体的所有文件引用
    /// </summary>
    [HttpGet("references")]
    public virtual async Task<ApiResult<IEnumerable<FileReferenceDto>>> GetReferencesByEntity(
        [FromQuery] string entityType,
        [FromQuery] Guid entityId)
    {
        var result = await FileReferenceService.GetReferencesByEntityAsync(entityType, entityId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取文件引用统计信息
    /// </summary>
    [HttpGet("references/statistics")]
    public virtual async Task<ApiResult<FileReferenceStatistics>> GetReferenceStatistics([FromQuery] string? entityType = null)
    {
        var result = await FileReferenceService.GetReferenceStatisticsAsync(entityType);
        return result.ToApiResult();
    }

    /// <summary>
    /// 同步单个文件的引用计数
    /// </summary>
    [HttpPost("{id:guid}/sync-reference-count")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<int>> SyncReferenceCount(Guid id)
    {
        var result = await FileReferenceService.SyncReferenceCountAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量同步所有文件的引用计数
    /// </summary>
    [HttpPost("sync-all-reference-counts")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<int>> SyncAllReferenceCounts()
    {
        var result = await FileReferenceService.SyncAllReferenceCountsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 验证文件的引用计数是否一致
    /// </summary>
    [HttpGet("{id:guid}/validate-reference-count")]
    public virtual async Task<ApiResult<bool>> ValidateReferenceCount(Guid id)
    {
        var result = await FileReferenceService.ValidateReferenceCountAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量确认引用
    /// </summary>
    [HttpPost("references/batch-confirm")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult> BatchConfirmReferences([FromBody] IEnumerable<FileReferenceInfo> references)
    {
        var result = await FileReferenceService.BatchConfirmReferencesAsync(references);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量更新引用
    /// </summary>
    [HttpPut("references/batch-update")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult> BatchUpdateReferences(
        [FromQuery] string entityType,
        [FromQuery] Guid entityId,
        [FromBody] Dictionary<string, IEnumerable<Guid>> request)
    {
        var result = await FileReferenceService.BatchUpdateReferencesAsync(entityType, entityId, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get storage usage for a specific user
    /// </summary>
    [HttpGet("usage/user/{userId:guid}")]
    public virtual async Task<ApiResult<UserStorageUsage>> GetUserStorageUsage(Guid userId)
    {
        var result = await FileStorageService.GetUserStorageUsageAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get top users by storage usage
    /// </summary>
    [HttpGet("usage/top-users")]
    public virtual async Task<ApiResult<IEnumerable<UserStorageUsage>>> GetTopUsersByStorage([FromQuery] int top = 20)
    {
        var result = await FileStorageService.GetTopUsersByStorageAsync(top);
        return result.ToApiResult();
    }

    /// <summary>
    /// Generate a presigned URL for a file (temporary public access)
    /// </summary>
    [HttpGet("{id:guid}/presigned-url")]
    public virtual async Task<ApiResult<string>> GetPresignedUrl(Guid id, [FromQuery] int expiresInSeconds = 3600, [FromQuery] string httpMethod = "GET")
    {
        var result = await FileStorageService.GetPresignedUrlAsync(id, expiresInSeconds, httpMethod);
        return result.ToApiResult();
    }

    // File integrity verification

    /// <summary>
    /// Verify integrity of a single file (checks physical existence + MD5 match)
    /// </summary>
    [HttpGet("{id:guid}/verify-integrity")]
    public virtual async Task<ApiResult<FileIntegrityResult>> VerifyFileIntegrity(Guid id)
    {
        var result = await FileStorageService.VerifyFileIntegrityAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Batch verify integrity of files (returns only problematic files in
    /// details). READ-ONLY diagnostic - no method-level action code, matching
    /// the single-file verify above; the class-level .view gate suffices.
    /// </summary>
    [HttpPost("verify-integrity")]
    public virtual async Task<ApiResult<BatchIntegrityResult>> BatchVerifyIntegrity([FromQuery] int maxFiles = 100)
    {
        var result = await FileStorageService.BatchVerifyIntegrityAsync(maxFiles);
        return result.ToApiResult();
    }

    // Share management

    /// <summary>
    /// Get all shares for a specific file
    /// </summary>
    [HttpGet("{id:guid}/shares")]
    public virtual async Task<ApiResult<IEnumerable<FileShareSummaryDto>>> GetSharesByFile(Guid id)
    {
        var result = await FileShareService.GetSharesByFileAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Query active shares with paging and filtering
    /// </summary>
    [HttpPost("shares/query")]
    public virtual async Task<ApiResult<IPagedList<FileShareSummaryDto>>> QueryActiveShares([FromBody] ActiveSharesQueryRequest request)
    {
        var result = await FileShareService.GetActiveSharesAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Batch revoke multiple shares
    /// </summary>
    [HttpPost("shares/batch-revoke")]
    [ApiAuthorize(PermissionName = "storage.file.delete")]
    public virtual async Task<ApiResult<int>> BatchRevokeShares([FromBody] IEnumerable<Guid> shareIds)
    {
        var result = await FileShareService.BatchRevokeSharesAsync(shareIds);
        return result.ToApiResult();
    }

    // File tags

    /// <summary>
    /// Set tags for a file
    /// </summary>
    [HttpPut("{id:guid}/tags")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<FileRecordDto>> SetFileTags(Guid id, [FromBody] SetFileTagsRequest request)
    {
        var result = await FileStorageService.SetFileTagsAsync(id, request.Tags);
        // 控制器边界：投影为安全 DTO，绝不把内部字段（Path 等）泄漏进 API 契约。
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// Get files by tag
    /// </summary>
    [HttpGet("by-tag/{tag}")]
    public virtual async Task<ApiResult<IPagedList<FileRecordDto>>> GetFilesByTag(string tag, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        var result = await FileStorageService.GetFilesByTagAsync(tag, pageIndex, pageSize);
        return result.Map(MapPaged).ToApiResult();
    }

    /// <summary>
    /// 把 FileRecord 分页列表投影为对外安全 DTO 分页列表
    /// </summary>
    private static IPagedList<FileRecordDto> MapPaged(IPagedList<FileRecord> paged) =>
        new PagedList<FileRecordDto>(
            paged.Items.Select(r => r.MapTo<FileRecordDto>()).ToList(),
            paged.PageIndex,
            paged.PageSize,
            paged.TotalCount);

    // File metadata

    /// <summary>
    /// Set metadata for a file (replaces existing metadata)
    /// </summary>
    [HttpPut("{id:guid}/metadata")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<FileRecordDto>> SetMetadata(Guid id, [FromBody] SetFileMetadataRequest request)
    {
        var result = await FileStorageService.SetMetadataAsync(id, request.Metadata);
        // 控制器边界：投影为安全 DTO，绝不把内部字段（Path 等）泄漏进 API 契约。
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// Get metadata for a file
    /// </summary>
    [HttpGet("{id:guid}/metadata")]
    public virtual async Task<ApiResult<Dictionary<string, string>>> GetMetadata(Guid id)
    {
        var result = await FileStorageService.GetMetadataAsync(id);
        return result.ToApiResult();
    }

    // File visibility

    /// <summary>
    /// Set whether a file is publicly readable.
    /// Public means readable by anyone (including unauthenticated callers) - use it
    /// for avatars and site assets, never for contracts, cheques or HR documents.
    /// </summary>
    [HttpPut("{id:guid}/visibility")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<FileRecordDto>> SetVisibility(Guid id, [FromBody] SetFileVisibilityRequest request)
    {
        var result = await FileStorageService.SetFileVisibilityAsync(id, request.IsPublic);
        // 控制器边界：投影为安全 DTO，绝不把内部字段（Path 等）泄漏进 API 契约。
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// Backfill the public flag from `[FileField(Public = true)]` declarations:
    /// every file referenced by a field declared public becomes publicly readable.
    /// Returns the number of files changed. Idempotent, and it never turns a file
    /// back into a private one.
    /// </summary>
    [HttpPost("sync-public-flags")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<int>> SyncPublicFlags()
    {
        var result = await FileStorageService.SyncPublicFlagsFromReferencesAsync();
        return result.ToApiResult();
    }
}
