namespace Tnzi.Storage.Controllers.Admin;

/// <summary>
/// 文件存储管理控制器基类
/// 提供文件管理类操作 API 端点，所有方法支持重写
/// </summary>
[Route("admin/files")]
public abstract class StorageAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IFileStorageService FileStorageService;
    protected readonly IFileReferenceService FileReferenceService;

    /// <summary>
    /// 初始化文件存储管理控制器基类
    /// </summary>
    protected StorageAdminControllerBase(
        IFileStorageService fileStorageService,
        IFileReferenceService fileReferenceService)
    {
        FileStorageService = Check.NotNull(fileStorageService);
        FileReferenceService = Check.NotNull(fileReferenceService);
    }

    /// <summary>
    /// 批量删除文件
    /// </summary>
    [HttpDelete("batch")]
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
    public virtual async Task<ApiResult<IEnumerable<FileRecord>>> GetTemporaryFiles([FromQuery] int? olderThanHours = null)
    {
        var olderThan = olderThanHours.HasValue
            ? TimeSpan.FromHours(olderThanHours.Value)
            : TimeSpan.FromHours(24);

        var result = await FileReferenceService.GetTemporaryFilesAsync(olderThan);
        return result.ToApiResult();
    }

    /// <summary>
    /// 查询文件列表（支持分页、筛选、排序）
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<FileRecord>>> QueryFiles([FromBody] FileQueryRequest request)
    {
        var result = await FileStorageService.QueryFilesAsync(request);
        return result.ToApiResult();
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
    public virtual async Task<ApiResult<int>> SyncReferenceCount(Guid id)
    {
        var result = await FileReferenceService.SyncReferenceCountAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量同步所有文件的引用计数
    /// </summary>
    [HttpPost("sync-all-reference-counts")]
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
    public virtual async Task<ApiResult> BatchConfirmReferences([FromBody] IEnumerable<FileReferenceInfo> references)
    {
        var result = await FileReferenceService.BatchConfirmReferencesAsync(references);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量更新引用
    /// </summary>
    [HttpPut("references/batch-update")]
    public virtual async Task<ApiResult> BatchUpdateReferences(
        [FromQuery] string entityType,
        [FromQuery] Guid entityId,
        [FromBody] Dictionary<string, IEnumerable<Guid>> request)
    {
        var result = await FileReferenceService.BatchUpdateReferencesAsync(entityType, entityId, request);
        return result.ToApiResult();
    }
}
