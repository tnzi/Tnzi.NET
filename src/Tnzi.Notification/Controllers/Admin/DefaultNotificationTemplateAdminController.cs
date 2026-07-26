using Tnzi.Template.Dtos;
using Tnzi.Template.Services;

namespace Tnzi.Notification.Controllers.Admin;

/// <summary>
/// Admin controller exposing a notification-scoped view over the generic
/// template store. All endpoints pin <c>Module = "Notification"</c> server-side
/// so the frontend admin page can CRUD notification templates without knowing
/// that the underlying store lives in the Tnzi.Template module.
///
/// <para>
/// If the Tnzi.Template module is not loaded (optional dep), every endpoint
/// returns HTTP 503 so the frontend surfaces a clean "backend gap" message
/// instead of a DI failure.
/// </para>
/// </summary>
[DefaultController]
[Route("admin/notification-templates")]
[ApiAuthorize(PermissionName = "notification.template.view")]
public class DefaultNotificationTemplateAdminController : ApiAdminControllerBase
{
    /// <summary>
    /// Module name pin - all notification templates live under this module in
    /// the shared Template store, regardless of what the client sends.
    /// </summary>
    protected const string NotificationModuleName = "Notification";

    protected readonly ITemplateStoreService? TemplateStoreService;

    public DefaultNotificationTemplateAdminController(ITemplateStoreService? templateStoreService = null)
    {
        TemplateStoreService = templateStoreService;
    }

    /// <summary>
    /// Query notification templates (paged). The <c>Module</c> filter is
    /// always overridden to <c>"Notification"</c>; other fields
    /// (<c>Category</c>, <c>IsActive</c>, <c>Keyword</c>) pass through.
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<TemplateInfoDto>>> GetAll([FromQuery] QueryTemplateRequest request)
    {
        if (TemplateStoreService is null)
        {
            return ApiResult<IPagedList<TemplateInfoDto>>.Error("Template module is not loaded", 503);
        }

        request.Module = NotificationModuleName;
        var result = await TemplateStoreService.QueryTemplatesAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get a single notification template by id. Returns 404 when the row
    /// exists but belongs to a different module (defence against id guessing
    /// from other admin panels).
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<TemplateDto>> GetById(Guid id)
    {
        if (TemplateStoreService is null)
        {
            return ApiResult<TemplateDto>.Error("Template module is not loaded", 503);
        }

        var result = await TemplateStoreService.GetTemplateByIdAsync(id);
        if (!result.Succeeded || result.Data is null)
        {
            return result.ToApiResult();
        }

        if (!string.Equals(result.Data.Module, NotificationModuleName, StringComparison.Ordinal))
        {
            return ApiResult<TemplateDto>.Error("Not found", 404);
        }

        return result.ToApiResult();
    }

    /// <summary>
    /// Create a notification template. The incoming request's
    /// <c>Module</c> is overridden to <c>"Notification"</c>; all other fields
    /// (name, category, subject, content, type, layouts, metadata) pass through.
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "notification.template.create")]
    public virtual async Task<ApiResult<TemplateDto>> Create([FromBody] CreateTemplateRequest request)
    {
        if (TemplateStoreService is null)
        {
            return ApiResult<TemplateDto>.Error("Template module is not loaded", 503);
        }

        request.Module = NotificationModuleName;
        var result = await TemplateStoreService.CreateTemplateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Update a notification template. Rejects with 404 if the target id
    /// belongs to a non-notification template; otherwise pins Module before
    /// delegating to the store service.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "notification.template.update")]
    public virtual async Task<ApiResult<TemplateDto>> Update(Guid id, [FromBody] UpdateTemplateRequest request)
    {
        if (TemplateStoreService is null)
        {
            return ApiResult<TemplateDto>.Error("Template module is not loaded", 503);
        }

        var existing = await TemplateStoreService.GetTemplateByIdAsync(id);
        if (!existing.Succeeded || existing.Data is null)
        {
            return existing.ToApiResult();
        }

        if (!string.Equals(existing.Data.Module, NotificationModuleName, StringComparison.Ordinal))
        {
            return ApiResult<TemplateDto>.Error("Not found", 404);
        }

        request.Module = NotificationModuleName;
        var result = await TemplateStoreService.UpdateTemplateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Delete a notification template. Scope-checks the target before deleting.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "notification.template.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        if (TemplateStoreService is null)
        {
            return ApiResult.Error("Template module is not loaded", 503);
        }

        var existing = await TemplateStoreService.GetTemplateByIdAsync(id);
        if (!existing.Succeeded || existing.Data is null)
        {
            return ApiResult.Error(existing.Message ?? "Not found", existing.Code ?? 404);
        }

        if (!string.Equals(existing.Data.Module, NotificationModuleName, StringComparison.Ordinal))
        {
            return ApiResult.Error("Not found", 404);
        }

        var result = await TemplateStoreService.DeleteTemplateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Batch delete notification templates. Each id is scope-checked; any
    /// non-notification id is silently skipped (keeps admin UX simple - the
    /// caller should never see foreign ids in the first place).
    /// </summary>
    [HttpDelete("batch")]
    [ApiAuthorize(PermissionName = "notification.template.delete")]
    public virtual async Task<ApiResult> DeleteBatch([FromBody] IEnumerable<Guid> ids)
    {
        if (TemplateStoreService is null)
        {
            return ApiResult.Error("Template module is not loaded", 503);
        }

        var scoped = new List<Guid>();
        foreach (var id in ids)
        {
            var existing = await TemplateStoreService.GetTemplateByIdAsync(id);
            if (existing.Succeeded
                && existing.Data is not null
                && string.Equals(existing.Data.Module, NotificationModuleName, StringComparison.Ordinal))
            {
                scoped.Add(id);
            }
        }

        if (scoped.Count == 0)
        {
            return Result.Success().ToApiResult();
        }

        var result = await TemplateStoreService.DeleteTemplatesAsync(scoped);
        return result.ToApiResult();
    }
}
