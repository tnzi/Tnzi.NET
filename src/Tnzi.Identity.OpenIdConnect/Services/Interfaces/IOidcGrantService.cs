namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// 授权（grant）：用户对客户端的同意。撤销一条授权，挂在它下面的刷新令牌随之失效。
/// </summary>
public interface IOidcGrantService
{
    /// <summary>当前用户的有效授权（「我授权过的应用」）。</summary>
    Task<Result<List<OidcGrantDto>>> GetMyGrantsAsync(CancellationToken cancellationToken = default);

    /// <summary>当前用户撤销自己对某个客户端的全部授权。</summary>
    Task<Result> RevokeMyGrantsAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>管理端分页查询。</summary>
    Task<Result<IPagedList<OidcGrantDto>>> GetPagedListAsync(OidcGrantQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>管理端撤销一条授权。</summary>
    Task<Result> RevokeAsync(Guid authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销某个用户对某个客户端的全部授权与令牌（供其它服务调用，例如消费方下架一个托管应用的某个成员）。
    /// </summary>
    /// <returns>被撤销的授权数。</returns>
    Task<int> RevokeAsync(Guid userId, string clientId, CancellationToken cancellationToken = default);

    /// <summary>同意页的展示信息（当前用户视角）。</summary>
    Task<Result<OidcConsentInfoDto>> GetConsentInfoAsync(string clientId, string? scope, CancellationToken cancellationToken = default);

    /// <summary>当前用户同意某个客户端申请的作用域：记为一条永久授权。</summary>
    Task<Result> GrantConsentAsync(GrantOidcConsentDto input, CancellationToken cancellationToken = default);
}
