namespace Tnzi.Documents.Signing.Controllers;

/// <summary>
/// 签署模板管理端点。
/// </summary>
[DefaultController]
[Route("admin/signing/templates")]
[ApiAuthorize(PermissionName = "signing.template.view")]
public class DefaultSigningTemplateAdminController : ApiAdminControllerBase
{
    protected readonly IEnvelopeTemplateService Templates;

    public DefaultSigningTemplateAdminController(IEnvelopeTemplateService templates)
    {
        Templates = Check.NotNull(templates);
    }

    /// <summary>分页列出模板。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<EnvelopeTemplateListDto>>> GetPaged(
        [FromQuery] EnvelopeTemplateQueryDto query, CancellationToken cancellationToken)
        => (await Templates.GetPagedAsync(query, cancellationToken)).ToApiResult();

    /// <summary>模板详情（含字段）。</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<EnvelopeTemplateDto>> Get(Guid id, CancellationToken cancellationToken)
        => (await Templates.GetAsync(id, cancellationToken)).ToApiResult();

    /// <summary>新建模板。</summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "signing.template.create")]
    public virtual async Task<ApiResult<EnvelopeTemplateDto>> Create(
        [FromBody] CreateEnvelopeTemplateDto input, CancellationToken cancellationToken)
        => (await Templates.CreateAsync(input, cancellationToken)).ToApiResult();

    /// <summary>更新模板（字段整体重建，版本号 +1）。</summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "signing.template.update")]
    public virtual async Task<ApiResult<EnvelopeTemplateDto>> Update(
        Guid id, [FromBody] UpdateEnvelopeTemplateDto input, CancellationToken cancellationToken)
        => (await Templates.UpdateAsync(id, input, cancellationToken)).ToApiResult();

    /// <summary>删除模板（被任何请求引用过则 409，改用停用）。</summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "signing.template.delete")]
    public virtual async Task<ApiResult> Delete(Guid id, CancellationToken cancellationToken)
        => (await Templates.DeleteAsync(id, cancellationToken)).ToApiResult();
}
