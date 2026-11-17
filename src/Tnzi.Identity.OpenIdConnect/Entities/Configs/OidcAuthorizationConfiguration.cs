namespace Tnzi.Identity.OpenIdConnect.Entities.Configs;

/// <summary><see cref="OidcAuthorization"/> 的 EF 映射（OpenIddict 的映射原样套用）。</summary>
public class OidcAuthorizationConfiguration : EntityTypeConfigurationBase<OidcAuthorization, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<OidcAuthorization> builder)
    {
        new OpenIddictEntityFrameworkCoreAuthorizationConfiguration<OidcAuthorization, OidcClient, OidcToken, Guid>().Configure(builder);
    }
}
