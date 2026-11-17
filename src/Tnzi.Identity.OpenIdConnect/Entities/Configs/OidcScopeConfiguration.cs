namespace Tnzi.Identity.OpenIdConnect.Entities.Configs;

/// <summary><see cref="OidcScope"/> 的 EF 映射（OpenIddict 的映射原样套用）。</summary>
public class OidcScopeConfiguration : EntityTypeConfigurationBase<OidcScope, Guid>
{
    /// <inheritdoc />
    public override void Configure(EntityTypeBuilder<OidcScope> builder)
    {
        new OpenIddictEntityFrameworkCoreScopeConfiguration<OidcScope, Guid>().Configure(builder);
    }
}
