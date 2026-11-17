namespace Tnzi.Identity.OpenIdConnect.Controllers;

/// <summary>
/// 用户端：我授权过的应用、撤销授权，以及同意页用到的两个端点。
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("oidc")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultOidcGrantController : ApiControllerBase
{
    /// <summary>授权服务。</summary>
    protected readonly IOidcGrantService GrantService;

    /// <summary>创建控制器。</summary>
    public DefaultOidcGrantController(IOidcGrantService grantService)
    {
        GrantService = Check.NotNull(grantService);
    }

    /// <summary>我授权过的应用（有效授权）。</summary>
    [HttpGet("grants")]
    public virtual async Task<ApiResult<List<OidcGrantDto>>> GetMyGrants()
        => (await GrantService.GetMyGrantsAsync(HttpContext.RequestAborted)).ToApiResult();

    /// <summary>撤销我对某个应用的全部授权：它手里的刷新令牌随之失效。</summary>
    [HttpDelete("grants/{clientId}")]
    public virtual async Task<ApiResult> RevokeMyGrants(string clientId)
        => (await GrantService.RevokeMyGrantsAsync(clientId, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>同意页的展示信息。</summary>
    [HttpGet("consents/{clientId}")]
    public virtual async Task<ApiResult<OidcConsentInfoDto>> GetConsentInfo(string clientId, [FromQuery] string? scope)
        => (await GrantService.GetConsentInfoAsync(clientId, scope, HttpContext.RequestAborted)).ToApiResult();

    /// <summary>用户点了「同意」：记一条永久授权，之后前端跳回 authorize。</summary>
    [HttpPost("consents")]
    public virtual async Task<ApiResult> GrantConsent([FromBody] GrantOidcConsentDto input)
        => (await GrantService.GrantConsentAsync(input, HttpContext.RequestAborted)).ToApiResult();
}
