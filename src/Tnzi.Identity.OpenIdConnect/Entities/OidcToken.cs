namespace Tnzi.Identity.OpenIdConnect.Entities;

/// <summary>
/// 已签发令牌的登记行（授权码、刷新令牌等）。撤销、轮换与「授权码只能用一次」都靠它。
/// </summary>
/// <remarks>行里有令牌载荷与引用标识，整类不进实体审计。</remarks>
[AuditIgnore]
public class OidcToken : OpenIddictEntityFrameworkCoreToken<Guid, OidcClient, OidcAuthorization>, IEntity<Guid>
{
    /// <inheritdoc />
    public object[] GetKeys() => [Id];
}
