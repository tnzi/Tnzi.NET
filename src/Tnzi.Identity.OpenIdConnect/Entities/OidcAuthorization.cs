namespace Tnzi.Identity.OpenIdConnect.Entities;

/// <summary>
/// 用户对某个客户端的一次授权（OpenIddict 的 authorization）。永久授权就是「用户同意过这个应用」；
/// 撤销它，挂在它下面的刷新令牌随之失效。
/// </summary>
[AuditIgnore]
public class OidcAuthorization : OpenIddictEntityFrameworkCoreAuthorization<Guid, OidcClient, OidcToken>, IEntity<Guid>
{
    /// <inheritdoc />
    public object[] GetKeys() => [Id];
}
