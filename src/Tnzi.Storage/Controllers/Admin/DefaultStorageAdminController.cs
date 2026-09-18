namespace Tnzi.Storage.Controllers.Admin;

/// <summary>
/// 文件存储管理控制器基类
/// 提供文件管理类操作 API 端点，所有方法支持重写
/// </summary>
/// <remarks>
/// ★ 三个分享管理端点的实现在可选包 <c>Tnzi.Storage.Workspace</c>，故
/// <see cref="FileShareService"/> 是<b>可空可选注入</b>：没加载时它们返回 501 并指名要加载的包，
/// 路由与其余端点一个字不变。理由与 <c>DefaultStorageController</c> 相同（见那里的类注释）。
/// </remarks>
/// <remarks>
/// ★ 三个分享管理端点的实现在可选包 <c>Tnzi.Storage.Workspace</c>，故
/// <see cref="FileShareService"/> 是<b>可空可选注入</b>：没加载时它们返回 501 并指名要加载的包，
/// 路由与其余端点一个字不变。理由与 <c>DefaultStorageController</c> 相同（见那里的类注释）。
/// </remarks>
[DefaultController]
[Route("admin/files")]
[ApiAuthorize(PermissionName = "storage.file.view")]
public class DefaultStorageAdminController : ApiAdminControllerBase
{
    protected readonly IFileStorageService FileStorageService;
    protected readonly IFileReferenceService FileReferenceService;

    /// <summary>分享链接服务；<c>null</c> = 未加载 <c>Tnzi.Storage.Workspace</c>。</summary>
    protected readonly IFileShareService? FileShareService;

    /// <summary>
    /// 初始化文件存储管理控制器基类
    /// </summary>
    public DefaultStorageAdminController(
        IFileStorageService fileStorageService,
        IFileReferenceService fileReferenceService,
        IFileShareService? fileShareService = null)
    {
        FileStorageService = Check.NotNull(fileStorageService);
        FileReferenceService = Check.NotNull(fileReferenceService);
        FileShareService = fileShareService;
    }

    /// <summary>工作区包缺席时的统一回答，措辞与 <c>DefaultStorageController</c> 一致。</summary>
    private const string WorkspaceMissing =
        "This capability requires the Tnzi.Storage.Workspace module, which this host has not loaded.";

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
    /// 同步单个<b>临时</b>文件的引用计数（按非临时引用行重算）。
    /// 正式上传与 MD5 复用的文件带隐式持有者、没有引用行可重建，这里答 400。
    /// </summary>
    [HttpPost("{id:guid}/sync-reference-count")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<int>> SyncReferenceCount(Guid id)
    {
        var result = await FileReferenceService.SyncReferenceCountAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量同步<b>全部临时文件</b>的引用计数（按非临时引用行重算），返回被改动的条数。
    /// 正式记录（未绑定的正式上传 / MD5 共享文件）一律不动：它们的隐式持有者不产生引用行，
    /// 按引用行重算会把它们归零并交给默认开启的孤儿回收物理删除。
    /// </summary>
    [HttpPost("sync-all-reference-counts")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<int>> SyncAllReferenceCounts()
    {
        var result = await FileReferenceService.SyncAllReferenceCountsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 验证文件的引用计数是否一致：临时记录要求与引用行数相等，正式记录只要求不低于引用行数
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
    /// Generate a presigned download URL for a file (temporary public read access)
    /// </summary>
    /// <remarks>
    /// ★ 动词写死为 GET，与用户端孪生同形。此前它把 <c>httpMethod</c> 开放给查询串：
    /// <c>?httpMethod=PUT</c> 从一个只挂类级 <c>storage.file.view</c> 的 GET 端点换到对象存储的
    /// 直传 URL，覆盖任意文件的字节而 <c>.update</c> 码与 UploadGuard 一次都没被问过；
    /// 按动词扫描的「写端点必须带方法级操作码」门禁看不见它。签发写凭据不是这个端点的事：
    /// 服务层 <see cref="IFileStorageService.GetPresignedUrlAsync"/> 对 PUT 另行要求写权限，
    /// 要直传的消费方在自己的写端点上调它。
    /// ★ 能力名与用户端共用同一个 <c>storage.presigned-url</c>：抑制键关一次全关，
    /// 否则关掉用户端那条之后这一条就是刚关掉那条能力的旁路。
    /// </remarks>
    [HttpGet("{id:guid}/presigned-url")]
    [SensitiveEndpoint(
        "storage.presigned-url",
        "Issues a URL that the object store serves directly. It bypasses application-layer authorization "
        + "entirely, and for cloud providers its lifetime is governed by the storage backend rather than by "
        + "Storage:SignedUrlTtlSeconds - so the framework can neither shorten nor revoke it once handed out. "
        + "Admin twin of the user endpoint; both share one capability name so one suppression closes both.")]
    public virtual async Task<ApiResult<string>> GetPresignedUrl(Guid id, [FromQuery] int expiresInSeconds = 3600)
    {
        var result = await FileStorageService.GetPresignedUrlAsync(id, expiresInSeconds, "GET");
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
        if (FileShareService == null)
            return Error<IEnumerable<FileShareSummaryDto>>(WorkspaceMissing, 501);

        var result = await FileShareService.GetSharesByFileAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Query active shares with paging and filtering
    /// </summary>
    [HttpPost("shares/query")]
    public virtual async Task<ApiResult<IPagedList<FileShareSummaryDto>>> QueryActiveShares([FromBody] ActiveSharesQueryRequest request)
    {
        if (FileShareService == null)
            return Error<IPagedList<FileShareSummaryDto>>(WorkspaceMissing, 501);

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
        if (FileShareService == null)
            return Error<int>(WorkspaceMissing, 501);

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

    /// <summary>
    /// 为没有缩略图的存量文件补画缩略图：位图与 PDF 首页（后者需加载可选包 <c>Tnzi.Documents</c>）。
    /// 幂等：已有缩略图的记录不动。一次最多处理 <c>MaxFiles</c> 条（默认 100，串行渲染），
    /// 循环调用直到 <c>Generated</c> 为 0；画不出来的记录留在 <c>FailedFileIds</c> 里，下次仍会再试。
    /// 请求体可省略（= 扫描全部、默认批量）。
    /// </summary>
    [HttpPost("backfill-thumbnails")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<ThumbnailBackfillResult>> BackfillThumbnails(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ThumbnailBackfillRequest? request = null)
    {
        request ??= new ThumbnailBackfillRequest();
        var result = await FileStorageService.BackfillThumbnailsAsync(request.FileIds, request.MaxFiles);
        return result.ToApiResult();
    }
}
