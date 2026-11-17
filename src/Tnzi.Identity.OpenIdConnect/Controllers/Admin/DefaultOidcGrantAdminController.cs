namespace Tnzi.Identity.OpenIdConnect.Controllers.Admin;

/// <summary>
/// 用户授权（grant）的管理端点：查看谁授权了哪个应用、撤销某一条。
/// </summary>
[DefaultController]
[Route("admin/oidc/grants")]
[ApiAuthorize(PermissionName = "identity.oidcGrant.view")]
public class DefaultOidcGrantAdminController : ApiAdminControllerBase
{
    /// <summary>授权服务。</summary>
    protected readonly IOidcGrantService GrantService;

    /// <summary>创建控制器。</summary>
    public DefaultOidcGrantAdminController(IOidcGrantService grantService)
    {
        GrantService = Check.NotNull(grantService);
    }

    /// <summary>分页查询。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<OidcGrantDto>>> GetPagedList([FromQuery] OidcGrantQueryDto query)
        => (await GrantService.GetPagedListAsync(query, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>撤销一条授权：它名下的刷新令牌随之失效。</summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "identity.oidcGrant.delete")]
    public virtual async Task<ApiResult> Revoke(Guid id)
        => (await GrantService.RevokeAsync(id, HttpContext.RequestAborted)).ToApiResult();
}
