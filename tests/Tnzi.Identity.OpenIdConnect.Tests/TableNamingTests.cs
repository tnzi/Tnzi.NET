using Tnzi.EFCore;
using Tnzi.Extensions;
using Tnzi.Identity.OpenIdConnect.Entities;
using Tnzi.Modules;

namespace Tnzi.Identity.OpenIdConnect.Tests;

/// <summary>
/// 四张表都带上 Identity_ 前缀。前缀是按实体所在程序集在模块容器里查的，模块少声明 TableNamePrefix
/// 一行，这些表会一声不吭地掉前缀。
/// </summary>
public sealed class TableNamingTests
{
    private sealed class PrefixProbe(IModuleContainer container) : TableNamePrefixConfiguration(container)
    {
        public string? PrefixFor(Type entityType) => GetTableNamePrefix(entityType);
    }

    [Theory]
    [InlineData(typeof(OidcClient))]
    [InlineData(typeof(OidcAuthorization))]
    [InlineData(typeof(OidcScope))]
    [InlineData(typeof(OidcToken))]
    public void EveryEntity_TakesTheIdentityPrefix(Type entityType)
    {
        var probe = new PrefixProbe(new ModuleContainer(
        [
            new ModuleDescriptor(typeof(IdentityModule), new IdentityModule()),
            new ModuleDescriptor(typeof(IdentityOpenIdConnectModule), new IdentityOpenIdConnectModule()),
        ]));

        probe.PrefixFor(entityType).ShouldBe("Identity");
    }
}
