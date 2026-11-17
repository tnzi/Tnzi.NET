namespace Tnzi.Identity.OpenIdConnect.Entities.Configs;

/// <summary><see cref="OidcToken"/> 的 EF 映射（OpenIddict 的映射原样套用）。</summary>
public class OidcTokenConfiguration : EntityTypeConfigurationBase<OidcToken, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<OidcToken> builder)
    {
        new OpenIddictEntityFrameworkCoreTokenConfiguration<OidcToken, OidcClient, OidcAuthorization, Guid>().Configure(builder);
    }
}
