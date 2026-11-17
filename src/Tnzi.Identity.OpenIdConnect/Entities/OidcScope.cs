namespace Tnzi.Identity.OpenIdConnect.Entities;

/// <summary>
/// 作用域定义（OpenIddict 的 scope）。本模块按配置在内存里登记作用域，这张表留给需要按作用域配置
/// 资源（audience）的消费方；不用时一直是空的。
/// </summary>
public class OidcScope : OpenIddictEntityFrameworkCoreScope<Guid>, IEntity<Guid>
{
    /// <inheritdoc />
    public object[] GetKeys() => [Id];
}
