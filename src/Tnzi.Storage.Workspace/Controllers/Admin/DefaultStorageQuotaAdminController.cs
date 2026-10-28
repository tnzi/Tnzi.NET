namespace Tnzi.Storage.Workspace.Controllers;

/// <summary>
/// 管理员按用户覆盖存储配额。一个用户的实际用量与生效配额见 <c>GET admin/files/quota/{userId}</c>。
/// </summary>
/// <remarks>
/// 类级 <c>storage.file.view</c>，写端点 <c>storage.file.update</c>（与文件管理同一组码，不另立权限）。
/// 覆盖值只在本模块是配额来源时生效：消费方注册了自己的 <c>IStorageAllowanceProvider</c> 时，这里写下的值没人读。
/// </remarks>
[DefaultController]
[Route("admin/storage/quotas")]
[ApiAuthorize(PermissionName = "storage.file.view")]
public class DefaultStorageQuotaAdminController : ApiAdminControllerBase
{
    protected readonly IStorageAllowanceService StorageAllowanceService;

    public DefaultStorageQuotaAdminController(IStorageAllowanceService storageAllowanceService)
    {
        StorageAllowanceService = Check.NotNull(storageAllowanceService);
    }

    /// <summary>
    /// List the users who have an allowance of their own
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<StorageAllowanceDto>>> GetList([FromQuery] StorageAllowanceQueryDto query)
    {
        var result = await StorageAllowanceService.GetListAsync(query, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get a user's own allowance (404 when the default applies)
    /// </summary>
    [HttpGet("{userId:guid}")]
    public virtual async Task<ApiResult<StorageAllowanceDto>> Get(Guid userId)
    {
        var result = await StorageAllowanceService.GetAsync(userId, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Set a user's allowance in bytes (null = unlimited for this user)
    /// </summary>
    [HttpPut("{userId:guid}")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult<StorageAllowanceDto>> Set(Guid userId, [FromBody] SetStorageAllowanceRequest request)
    {
        var result = await StorageAllowanceService.SetAsync(userId, request, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Remove a user's own allowance so the default applies again
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [ApiAuthorize(PermissionName = "storage.file.update")]
    public virtual async Task<ApiResult> Remove(Guid userId)
    {
        var result = await StorageAllowanceService.RemoveAsync(userId, HttpContext.RequestAborted);
        return result.ToApiResult();
    }
}
