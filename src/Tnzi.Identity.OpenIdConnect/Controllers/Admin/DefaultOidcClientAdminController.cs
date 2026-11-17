namespace Tnzi.Identity.OpenIdConnect.Controllers.Admin;

/// <summary>
/// OpenID Connect 客户端登记簿的管理端点。
/// </summary>
[DefaultController]
[Route("admin/oidc/clients")]
[ApiAuthorize(PermissionName = "identity.oidcClient.view")]
public class DefaultOidcClientAdminController : ApiAdminControllerBase
{
    /// <summary>客户端服务。</summary>
    protected readonly IOidcClientService ClientService;

    /// <summary>创建控制器。</summary>
    public DefaultOidcClientAdminController(IOidcClientService clientService)
    {
        ClientService = Check.NotNull(clientService);
    }

    /// <summary>分页查询。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<OidcClientDto>>> GetPagedList([FromQuery] OidcClientQueryDto query)
        => (await ClientService.GetPagedListAsync(query, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>详情。</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<OidcClientDto>> Get(Guid id)
        => (await ClientService.GetAsync(id, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>登记客户端。机密客户端的明文密钥只在这次响应里出现。</summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "identity.oidcClient.create")]
    public virtual async Task<ApiResult<OidcClientSecretDto>> Create([FromBody] CreateOidcClientDto input)
        => (await ClientService.CreateAsync(input, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>修改客户端。</summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "identity.oidcClient.update")]
    public virtual async Task<ApiResult<OidcClientDto>> Update(Guid id, [FromBody] UpdateOidcClientDto input)
        => (await ClientService.UpdateAsync(id, input, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>启用 / 停用。停用撤销它名下的全部授权与令牌。</summary>
    [HttpPut("{id:guid}/enabled")]
    [ApiAuthorize(PermissionName = "identity.oidcClient.update")]
    public virtual async Task<ApiResult> SetEnabled(Guid id, [FromBody] SetOidcClientEnabledDto input)
        => (await ClientService.SetEnabledAsync(id, Check.NotNull(input).Enabled, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>轮换客户端密钥，旧密钥立即失效。</summary>
    [HttpPost("{id:guid}/rotate-secret")]
    [ApiAuthorize(PermissionName = "identity.oidcClient.update")]
    public virtual async Task<ApiResult<OidcClientSecretDto>> RotateSecret(Guid id)
        => (await ClientService.RotateSecretAsync(id, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>删除客户端（连同授权与令牌）。</summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "identity.oidcClient.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
        => (await ClientService.DeleteAsync(id, HttpContext.RequestAborted)).ToApiResult();
}
