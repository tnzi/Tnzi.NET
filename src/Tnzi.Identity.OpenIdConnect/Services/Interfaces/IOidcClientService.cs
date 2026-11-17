namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// 客户端登记簿：管理端与消费方代码共用的同一个入口（例如消费方为每个托管应用登记一个客户端）。
/// </summary>
public interface IOidcClientService
{
    /// <summary>登记客户端。机密客户端的明文密钥只在返回值里出现这一次。</summary>
    Task<Result<OidcClientSecretDto>> CreateAsync(CreateOidcClientDto input, CancellationToken cancellationToken = default);

    /// <summary>修改客户端。</summary>
    Task<Result<OidcClientDto>> UpdateAsync(Guid id, UpdateOidcClientDto input, CancellationToken cancellationToken = default);

    /// <summary>按记录 Id 取客户端。</summary>
    Task<Result<OidcClientDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>分页查询。</summary>
    Task<Result<IPagedList<OidcClientDto>>> GetPagedListAsync(OidcClientQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>按 client_id 取客户端（供其它服务调用，查不到返回 null）。</summary>
    Task<OidcClientDto?> FindByClientIdAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>按外部引用取客户端（供其它服务调用）。</summary>
    Task<IReadOnlyList<OidcClientDto>> FindByExternalReferenceAsync(string externalReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用或停用。停用时撤销它名下的全部授权与令牌：已签出的刷新令牌立刻失效，再次启用也不会复活。
    /// </summary>
    Task<Result> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>为机密客户端换一个新密钥，旧密钥立即失效。返回新的明文密钥。</summary>
    Task<Result<OidcClientSecretDto>> RotateSecretAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>删除客户端（连同它的授权与令牌）。</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
